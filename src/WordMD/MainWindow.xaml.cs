using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace WordMD;

public sealed partial class MainWindow : Window
{
    // Tabs replace the old single-document state.
    private readonly List<TabModel> _tabs = new();
    private TabModel? _activeTab;
    private bool _suppressTabSelection;

    private bool _editorReady;
    private string _mode = "formatted";
    private string _theme = "light";
    private double _zoom = 1.0;
    private bool _scrollSync = true;
    private bool _lockToSource = true;

    private readonly SettingsStore _settings = new();
    private readonly RecoveryStore _recovery = new();
    private TelemetryService _telemetry = null!;
    private System.Threading.Timer? _autosaveTimer;

    // Pending operation hook: when JS returns documentText for the active doc.
    // Each request gets a unique id so concurrent autosave + user-save can't
    // overwrite each other's completion source.
    private readonly Dictionary<string, TaskCompletionSource<string>> _pendingDocumentRequests = new();
    private long _nextRequestId;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        _settings.Load();
        _telemetry = new TelemetryService(_settings);
        TelemetryMenuItem.IsChecked = _settings.TelemetryOptIn;
        _telemetry.TrackEvent("app.start", new() { ["mode"] = _settings.Mode, ["theme"] = _settings.Theme });
        _mode = _settings.Mode ?? "split";
        _theme = _settings.Theme ?? "light";
        _zoom = _settings.Zoom > 0 ? _settings.Zoom : 1.0;
        _scrollSync = _settings.ScrollSync;
        _lockToSource = _settings.LockToSource;
        // Invariant: Lock to Source requires Sync Scrolling.
        if (_lockToSource && !_scrollSync) { _lockToSource = false; _settings.LockToSource = false; }
        ScrollSyncMenu.IsChecked = _scrollSync;
        LockToSourceMenu.IsChecked = _lockToSource;
        AutoReloadMenuItem.IsChecked = _settings.AutoReloadOnExternalChange;

        // If launched with a file path on the command line / file association, remember it
        // and open it once the editor is ready.
        _pendingStartupFilePath = !string.IsNullOrEmpty(App.StartupFilePath) ? App.StartupFilePath : null;

        SyncModeButtons();
        SyncThemeMenu();
        ApplyShellTheme(_theme);

        // Keep caption buttons legible if the system theme flips while we're
        // in "system" mode (or if any other ActualTheme change occurs).
        RootGrid.ActualThemeChanged += (_, _) =>
            UpdateCaptionButtonColors(RootGrid.ActualTheme == ElementTheme.Dark);

        _ = InitializeWebViewAsync();
        StartAutosaveTimer();
        UpdateRecentFilesMenu();
        UpdateTitle();

        // Drag-drop a .md file onto the window
        RootGrid.AllowDrop = true;
        RootGrid.DragOver += (_, e) =>
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        };
        RootGrid.Drop += async (_, e) =>
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;
            var items = await e.DataView.GetStorageItemsAsync();
            var first = items.OfType<Windows.Storage.StorageFile>().FirstOrDefault();
            if (first == null) return;
            await OpenFileInNewTabAsync(first.Path);
        };
    }

    private string? _pendingStartupFilePath;

    // External file-open requests (from second-instance redirection) that
    // arrived before the WebView editor finished initializing. Drained in
    // the editorReady handler.
    private readonly List<string> _pendingExternalFiles = new();

    // ---- WebView2 ----
    private async Task InitializeWebViewAsync()
    {
        await EditorView.EnsureCoreWebView2Async();
        var folder = Path.Combine(AppContext.BaseDirectory, "WebContent");
        EditorView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "appassets.local", folder, CoreWebView2HostResourceAccessKind.Allow);
        EditorView.CoreWebView2.WebMessageReceived += OnWebMessage;
        EditorView.CoreWebView2.Settings.AreDevToolsEnabled = true;
        EditorView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        EditorView.Source = new Uri("https://appassets.local/editor.html");
    }

    private void OnWebMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var doc = JsonDocument.Parse(args.WebMessageAsJson);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();
            var payload = root.TryGetProperty("payload", out var p) ? p : default;
            switch (type)
            {
                case "editorReady":
                    _editorReady = true;
                    PostMode(_mode);
                    PostTheme(_theme);
                    PostZoom(_zoom);
                    PostScrollSync(_scrollSync);
                    PostLockToSource(_lockToSource);
                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        if (!string.IsNullOrEmpty(_pendingStartupFilePath))
                        {
                            var p = _pendingStartupFilePath;
                            _pendingStartupFilePath = null;
                            await OpenFileInNewTabAsync(p);
                        }
                        else
                        {
                            NewBlankTab();
                            if (_recovery.HasRecovery())
                                _ = MaybeRestoreRecoveryAsync();
                        }

                        // Drain any external-open requests that arrived while
                        // the editor was still initializing.
                        if (_pendingExternalFiles.Count > 0)
                        {
                            var queued = _pendingExternalFiles.ToList();
                            _pendingExternalFiles.Clear();
                            foreach (var ext in queued)
                                await OpenFileFromExternalAsync(ext);
                        }
                    });
                    break;
                case "documentDirty":
                {
                    var did = payload.TryGetProperty("docId", out var ddi) ? ddi.GetString() : null;
                    var isDirty = payload.TryGetProperty("dirty", out var d) && d.GetBoolean();
                    var tab = _tabs.FirstOrDefault(t => t.DocId == did);
                    if (tab != null) { tab.IsDirty = isDirty; SyncTabHeader(tab); UpdateTitle(); }
                    break;
                }
                case "documentStats":
                    if (payload.ValueKind == JsonValueKind.Object)
                    {
                        var did = payload.TryGetProperty("docId", out var ddi) ? ddi.GetString() : null;
                        // Only update status bar for the active doc
                        if (did != null && did == _activeTab?.DocId)
                        {
                            if (payload.TryGetProperty("words", out var w)) StatusWords.Text = $"Words: {w.GetInt32()}";
                            if (payload.TryGetProperty("chars", out var c)) StatusChars.Text = $"Chars: {c.GetInt32()}";
                            if (payload.TryGetProperty("readMinutes", out var r)) StatusRead.Text = $"Read: {r.GetInt32()} min";
                            if (payload.TryGetProperty("eol", out var eolv)) StatusEol.Text = eolv.GetString() ?? "CRLF";
                        }
                    }
                    break;
                case "documentText":
                {
                    var rid = payload.TryGetProperty("requestId", out var rr) ? rr.GetString() : null;
                    var text = payload.TryGetProperty("text", out var t) ? (t.GetString() ?? "") : "";
                    if (rid != null && _pendingDocumentRequests.TryGetValue(rid, out var tcs))
                    {
                        _pendingDocumentRequests.Remove(rid);
                        tcs.TrySetResult(text);
                    }
                    break;
                }
                case "cursorChanged":
                    // optional: surface line/col
                    break;
                case "hostCommand":
                {
                    var cmd = payload.TryGetProperty("command", out var c) ? c.GetString() : null;
                    if (!string.IsNullOrEmpty(cmd)) HandleHostCommand(cmd);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WebMessage error: {ex.Message}");
        }
    }

    private void Post(string type, JsonNode? payload)
    {
        if (EditorView.CoreWebView2 == null) return;
        // JsonObject is trim-safe (no reflection); JsonSerializer<T> with anonymous
        // payloads is not, which is why we build the envelope explicitly.
        var envelope = new JsonObject { ["type"] = type };
        if (payload != null) envelope["payload"] = payload;
        EditorView.CoreWebView2.PostWebMessageAsJson(envelope.ToJsonString());
    }

    // ---- Keyboard shortcuts from the WebView editor ----
    // When focus is in the WebView2 (which is virtually always, since it hosts
    // the editor surface), keyboard input is consumed by Edge before WinUI's
    // MenuFlyoutItem KeyboardAccelerator infrastructure ever sees it -- so the
    // XAML accelerators (Ctrl+S etc.) silently don't fire. The bundled web
    // editor installs a global keydown listener that forwards file-level
    // shortcuts to the host as a "hostCommand" message, routed below to the
    // existing menu handlers.
    private void HandleHostCommand(string command)
    {
        // Dispatch instead of invoking inline so we don't run host UI work
        // (file pickers, ContentDialogs) inside the WebView2 message callback.
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (command)
            {
                case "save":      MenuSave_Click(this, null!); break;
                case "saveAs":    MenuSaveAs_Click(this, null!); break;
                case "new":       MenuNew_Click(this, null!); break;
                case "open":      MenuOpen_Click(this, null!); break;
                case "newTab":    MenuNewTab_Click(this, null!); break;
                case "closeTab":  MenuCloseTab_Click(this, null!); break;
                case "find":      MenuFind_Click(this, null!); break;
                case "replace":   MenuReplace_Click(this, null!); break;
                case "reload":    MenuReload_Click(this, null!); break;
                case "userGuide": MenuUserGuide_Click(this, null!); break;
                case "feedback":  MenuFeedback_Click(this, null!); break;
            }
        });
    }

    private void PostMode(string mode) => Post("setMode", new JsonObject { ["mode"] = mode });
    private void PostTheme(string theme) => Post("setTheme", new JsonObject { ["theme"] = theme });
    private void PostZoom(double scale) => Post("setZoom", new JsonObject { ["scale"] = scale });
    private void PostScrollSync(bool enabled) => Post("setScrollSync", new JsonObject { ["enabled"] = enabled });
    private void PostLockToSource(bool enabled) => Post("setLockToSource", new JsonObject { ["enabled"] = enabled });
    private void PostFormat(JsonNode payload) => Post("applyFormat", payload);

    private async Task<string> RequestDocumentTextAsync()
    {
        if (_activeTab == null) return string.Empty;
        var requestId = System.Threading.Interlocked.Increment(ref _nextRequestId).ToString();
        var tcs = new TaskCompletionSource<string>();
        _pendingDocumentRequests[requestId] = tcs;
        Post("getDocument", new JsonObject { ["requestId"] = requestId });
        var done = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        if (done == tcs.Task) return tcs.Task.Result;
        // Timeout — drop the pending entry so it doesn't leak.
        _pendingDocumentRequests.Remove(requestId);
        return string.Empty;
    }

    // ---- Tabs ----
    private TabModel NewBlankTab()
    {
        var tab = new TabModel { DisplayName = "Untitled.md" };
        AddTab(tab, text: "");
        return tab;
    }

    private void AddTab(TabModel tab, string text, string lineEnding = "\r\n")
    {
        _tabs.Add(tab);
        var item = new TabViewItem
        {
            Header = tab.TabHeader,
            IconSource = new SymbolIconSource { Symbol = Symbol.Document },
            Tag = tab.DocId,
            Content = null,
        };
        tab.View = item;
        Post("createDocument", new JsonObject { ["docId"] = tab.DocId, ["text"] = text, ["lineEnding"] = lineEnding });
        _suppressTabSelection = true;
        DocTabs.TabItems.Add(item);
        DocTabs.SelectedItem = item;
        _suppressTabSelection = false;
        SwitchToTab(tab);
    }

    private void SwitchToTab(TabModel tab)
    {
        _activeTab = tab;
        Post("switchDocument", new JsonObject { ["docId"] = tab.DocId });
        StatusFile.Text = tab.DisplayName;
        StatusEol.Text = tab.LineEnding == "\r\n" ? "CRLF" : "LF";
        StatusEnc.Text = tab.Encoding;
        UpdateTitle();
    }

    private void SyncTabHeader(TabModel tab)
    {
        if (tab.View != null) tab.View.Header = tab.TabHeader;
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTabSelection) return;
        if (DocTabs.SelectedItem is not TabViewItem item) return;
        var docId = item.Tag as string;
        var tab = _tabs.FirstOrDefault(t => t.DocId == docId);
        if (tab != null && tab != _activeTab) SwitchToTab(tab);
    }

    private void Tabs_AddTabButtonClick(TabView sender, object args) => NewBlankTab();

    private async void Tabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        var docId = (args.Item as TabViewItem)?.Tag as string;
        var tab = _tabs.FirstOrDefault(t => t.DocId == docId);
        if (tab == null) return;
        await CloseTabAsync(tab);
    }

    private async Task CloseTabAsync(TabModel tab)
    {
        if (tab.IsDirty)
        {
            // Switch to the dirty tab so the prompt context matches
            if (tab != _activeTab)
            {
                _suppressTabSelection = true;
                DocTabs.SelectedItem = tab.View;
                _suppressTabSelection = false;
                SwitchToTab(tab);
            }
            var dlg = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Unsaved changes",
                Content = $"\"{tab.DisplayName}\" has unsaved changes. Save before closing?",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Discard",
                CloseButtonText = "Cancel",
            };
            var r = await dlg.ShowAsync();
            if (r == ContentDialogResult.None) return;
            if (r == ContentDialogResult.Primary)
            {
                if (!await SaveAsync(false)) return;
            }
        }
        // Remove from model + view
        DetachWatcher(tab);
        _tabs.Remove(tab);
        if (tab.View != null) DocTabs.TabItems.Remove(tab.View);
        Post("closeDocument", new JsonObject { ["docId"] = tab.DocId });
        if (_activeTab == tab) _activeTab = null;
        if (_tabs.Count == 0) NewBlankTab();
    }

    private void MenuNewTab_Click(object sender, RoutedEventArgs e) => NewBlankTab();

    private async void MenuCloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab != null) await CloseTabAsync(_activeTab);
    }

    private async Task OpenFileInNewTabAsync(string path)
    {
        // If the only tab is a clean Untitled, replace it instead of stacking.
        TabModel? target = null;
        if (_tabs.Count == 1 && _activeTab != null && !_activeTab.IsDirty && _activeTab.FilePath == null)
            target = _activeTab;

        try
        {
            var bytes = await ReadFileBytesWithRetryAsync(path);
            var (text, encoding) = DecodeBytes(bytes);
            var lineEnding = text.Contains("\r\n") ? "\r\n" : "\n";
            if (target != null)
            {
                target.FilePath = path;
                target.DisplayName = Path.GetFileName(path);
                target.LineEnding = lineEnding;
                target.Encoding = encoding;
                target.IsDirty = false;
                SyncTabHeader(target);
                Post("createDocument", new JsonObject { ["docId"] = target.DocId, ["text"] = text, ["lineEnding"] = lineEnding });
                Post("switchDocument", new JsonObject { ["docId"] = target.DocId });
                _activeTab = target;
            }
            else
            {
                var tab = new TabModel
                {
                    FilePath = path,
                    DisplayName = Path.GetFileName(path),
                    LineEnding = lineEnding,
                    Encoding = encoding,
                };
                AddTab(tab, text, lineEnding);
            }
            StatusFile.Text = Path.GetFileName(path);
            StatusEol.Text = lineEnding == "\r\n" ? "CRLF" : "LF";
            StatusEnc.Text = encoding;
            _settings.AddRecent(path);
            _settings.Save();
            UpdateRecentFilesMenu();
            UpdateTitle();
            if (_activeTab != null) AttachWatcher(_activeTab);

            // Apply the remembered mode for this file (if any) — overrides the global default.
            var remembered = _settings.GetRememberedDocMode(path);
            if (!string.IsNullOrEmpty(remembered) && remembered != _mode)
                SetMode(remembered);

            _telemetry.TrackEvent("file.open");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Open failed", ex.Message);
        }
    }

    // ---- File operations (operate on the active tab) ----
    private void MenuNew_Click(object sender, RoutedEventArgs e) => NewBlankTab();

    private async void MenuOpen_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".md");
        picker.FileTypeFilter.Add(".markdown");
        picker.FileTypeFilter.Add(".mdown");
        picker.FileTypeFilter.Add(".txt");
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        await OpenFileInNewTabAsync(file.Path);
    }

    private async void MenuSave_Click(object sender, RoutedEventArgs e) => await SaveAsync(false);
    private async void MenuSaveAs_Click(object sender, RoutedEventArgs e) => await SaveAsync(true);

    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (_activeTab == null) return false;
        var tab = _activeTab;
        // Reentrancy guard: ignore overlapping save requests (e.g. Ctrl+S key
        // repeat, double menu clicks). Returning false here keeps the caller's
        // contract intact without firing a second writer at the same file.
        if (tab.IsSaving) return false;
        var path = tab.FilePath;
        if (saveAs || string.IsNullOrEmpty(path))
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.SuggestedFileName = string.IsNullOrEmpty(tab.FilePath) ? "Untitled" : Path.GetFileNameWithoutExtension(tab.FilePath);
            picker.FileTypeChoices.Add("Markdown", new List<string> { ".md" });
            picker.FileTypeChoices.Add("Markdown (.markdown)", new List<string> { ".markdown" });
            var file = await picker.PickSaveFileAsync();
            if (file == null) return false;
            path = file.Path;
        }
        var text = await RequestDocumentTextAsync();
        // Mark the tab as saving BEFORE the write so the FileSystemWatcher
        // events fired during the write don't trip the "file changed on disk"
        // prompt against our own save. Cleared in finally only after the
        // post-save timestamp/size have been refreshed on the tab.
        tab.IsSaving = true;
        try
        {
            await File.WriteAllTextAsync(path!, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            tab.FilePath = path;
            tab.DisplayName = Path.GetFileName(path!);
            tab.IsDirty = false;
            tab.ExternallyChanged = false;
            // Refresh known on-disk timestamp/size so the watcher doesn't treat
            // our own save as an external change.
            try
            {
                var fi = new FileInfo(path!);
                tab.LastWriteTimeUtc = fi.LastWriteTimeUtc;
                tab.LastSize = fi.Length;
            }
            catch { }
            SyncTabHeader(tab);
            StatusFile.Text = tab.DisplayName;
            _settings.AddRecent(path!);
            _settings.Save();
            UpdateRecentFilesMenu();
            UpdateTitle();
            AttachWatcher(tab);
            _recovery.Clear();
            _telemetry.TrackEvent("file.save");
            return true;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Save failed", ex.Message);
            return false;
        }
        finally
        {
            tab.IsSaving = false;
        }
    }

    private async void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        // Confirm any dirty tab before exit
        foreach (var t in _tabs.ToList())
        {
            if (t.IsDirty)
            {
                if (t != _activeTab)
                {
                    _suppressTabSelection = true;
                    DocTabs.SelectedItem = t.View;
                    _suppressTabSelection = false;
                    SwitchToTab(t);
                }
                if (!await ConfirmDiscardAsync()) return;
            }
        }
        Application.Current.Exit();
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (_activeTab == null || !_activeTab.IsDirty) return true;
        var dlg = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Unsaved changes",
            Content = $"\"{_activeTab.DisplayName}\" has unsaved changes. Save before continuing?",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Discard",
            CloseButtonText = "Cancel"
        };
        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.Primary) return await SaveAsync(false);
        if (result == ContentDialogResult.Secondary) return true;
        return false;
    }

    private static (string text, string encoding) DecodeBytes(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "UTF-8 BOM");
        return (Encoding.UTF8.GetString(bytes), "UTF-8");
    }

    private void UpdateRecentFilesMenu()
    {
        OpenRecentMenu.Items.Clear();
        foreach (var path in _settings.RecentFiles)
        {
            var item = new MenuFlyoutItem { Text = path };
            item.Click += async (_, __) => { await OpenFileInNewTabAsync(path); };
            OpenRecentMenu.Items.Add(item);
        }
        if (_settings.RecentFiles.Count == 0)
        {
            OpenRecentMenu.Items.Add(new MenuFlyoutItem { Text = "(empty)", IsEnabled = false });
        }
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "OK"
        };
        await dlg.ShowAsync();
    }

    private void UpdateTitle()
    {
        var name = _activeTab?.DisplayName ?? "Untitled.md";
        var dirty = _activeTab?.IsDirty ?? false;
        Title = (dirty ? "● " : "") + name + " — WordMD";
        AppTitleBar.Title = Title;
    }

    // ---- Modes ----
    private void ModeSource_Click(object sender, RoutedEventArgs e)    => SetMode("source");
    private void ModeFormatted_Click(object sender, RoutedEventArgs e) => SetMode("formatted");
    private void ModeSplit_Click(object sender, RoutedEventArgs e)     => SetMode("split");
    private void MenuToggleMode_Click(object sender, RoutedEventArgs e)
    {
        var next = _mode switch { "source" => "formatted", "formatted" => "split", _ => "source" };
        SetMode(next);
    }

    private void SetMode(string mode)
    {
        _mode = mode;
        _settings.Mode = mode;
        // Remember per-file mode for the active doc (if it has a path).
        if (!string.IsNullOrEmpty(_activeTab?.FilePath))
            _settings.RememberDocMode(_activeTab.FilePath, mode);
        _settings.Save();
        SyncModeButtons();
        if (_editorReady) PostMode(mode);
    }

    private void SyncModeButtons()
    {
        ModeSourceBtn.IsChecked    = _mode == "source";
        ModeFormattedBtn.IsChecked = _mode == "formatted";
        ModeSplitBtn.IsChecked     = _mode == "split";
    }

    // ---- Themes ----
    private void MenuThemeLight_Click(object sender, RoutedEventArgs e)  => SetTheme("light");
    private void MenuThemeDark_Click(object sender, RoutedEventArgs e)   => SetTheme("dark");
    private void MenuThemeSystem_Click(object sender, RoutedEventArgs e) => SetTheme("system");

    private void SetTheme(string theme)
    {
        _theme = theme;
        _settings.Theme = theme; _settings.Save();
        SyncThemeMenu();
        ApplyShellTheme(theme);
        var effective = theme;
        if (theme == "system")
        {
            var rk = (RootGrid.ActualTheme == ElementTheme.Dark) ? "dark" : "light";
            effective = rk;
        }
        if (_editorReady) PostTheme(effective);
    }

    /// <summary>Applies the requested theme to the WinUI shell (RootGrid) and
    /// keeps the system caption buttons (min/max/close) in a matching color so
    /// they remain legible. Without this, switching to Light leaves the
    /// caption-button glyphs in their previous (white) color over a light
    /// title bar.</summary>
    private void ApplyShellTheme(string theme)
    {
        if (RootGrid == null) return;
        var elementTheme = theme switch
        {
            "light" => ElementTheme.Light,
            "dark"  => ElementTheme.Dark,
            _       => ElementTheme.Default
        };
        RootGrid.RequestedTheme = elementTheme;

        // Resolve "system" to the actual app-level theme so caption buttons
        // are correctly colored before ActualTheme propagates through layout.
        var isDark = elementTheme switch
        {
            ElementTheme.Dark  => true,
            ElementTheme.Light => false,
            _                  => Application.Current.RequestedTheme == ApplicationTheme.Dark
        };
        UpdateCaptionButtonColors(isDark);
    }

    private void UpdateCaptionButtonColors(bool isDark)
    {
        try
        {
            var tb = AppWindow?.TitleBar;
            if (tb == null) return;

            var fg = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            var inactiveFg = isDark
                ? Windows.UI.Color.FromArgb(0xFF, 0xAA, 0xAA, 0xAA)
                : Windows.UI.Color.FromArgb(0xFF, 0x66, 0x66, 0x66);
            var hoverBg = isDark
                ? Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
                : Windows.UI.Color.FromArgb(0x18, 0x00, 0x00, 0x00);
            var pressedBg = isDark
                ? Windows.UI.Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)
                : Windows.UI.Color.FromArgb(0x2A, 0x00, 0x00, 0x00);

            tb.ButtonForegroundColor         = fg;
            tb.ButtonInactiveForegroundColor = inactiveFg;
            tb.ButtonHoverForegroundColor    = fg;
            tb.ButtonPressedForegroundColor  = fg;
            tb.ButtonBackgroundColor         = Microsoft.UI.Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonHoverBackgroundColor    = hoverBg;
            tb.ButtonPressedBackgroundColor  = pressedBg;
        }
        catch { /* AppWindow / TitleBar may be unavailable on some hosts */ }
    }

    private void SyncThemeMenu()
    {
        ThemeLightMenu.IsChecked  = _theme == "light";
        ThemeDarkMenu.IsChecked   = _theme == "dark";
        ThemeSystemMenu.IsChecked = _theme == "system";
    }

    // ---- Zoom ----
    private void MenuZoomIn_Click(object sender, RoutedEventArgs e)
        { _zoom = Math.Min(3.0, _zoom + 0.1); ApplyZoom(); }
    private void MenuZoomOut_Click(object sender, RoutedEventArgs e)
        { _zoom = Math.Max(0.5, _zoom - 0.1); ApplyZoom(); }
    private void MenuZoomReset_Click(object sender, RoutedEventArgs e)
        { _zoom = 1.0; ApplyZoom(); }
    private void ApplyZoom()
    {
        _settings.Zoom = _zoom; _settings.Save();
        if (_editorReady) PostZoom(_zoom);
    }

    // ---- Find / Replace ----
    private void MenuFind_Click(object sender, RoutedEventArgs e)    => Post("openFind", null);
    private void MenuReplace_Click(object sender, RoutedEventArgs e) => Post("openReplace", null);

    // ---- Scroll-sync toggle (split mode) ----
    private void MenuScrollSyncToggle_Click(object sender, RoutedEventArgs e)
    {
        _scrollSync = ScrollSyncMenu.IsChecked;
        _settings.ScrollSync = _scrollSync;

        // Lock to Source requires Sync Scrolling. If user turns Sync off, Lock is meaningless;
        // turn it off too so the menu reflects the actual active behavior.
        if (!_scrollSync && _lockToSource)
        {
            _lockToSource = false;
            _settings.LockToSource = false;
            LockToSourceMenu.IsChecked = false;
            if (_editorReady) PostLockToSource(false);
        }

        _settings.Save();
        if (_editorReady) PostScrollSync(_scrollSync);
    }

    private void MenuLockToSourceToggle_Click(object sender, RoutedEventArgs e)
    {
        _lockToSource = LockToSourceMenu.IsChecked;
        _settings.LockToSource = _lockToSource;

        // Lock to Source requires Sync Scrolling. If user enables Lock while Sync is off,
        // also enable Sync so the setting actually does something.
        if (_lockToSource && !_scrollSync)
        {
            _scrollSync = true;
            _settings.ScrollSync = true;
            ScrollSyncMenu.IsChecked = true;
            if (_editorReady) PostScrollSync(true);
        }

        _settings.Save();
        if (_editorReady) PostLockToSource(_lockToSource);
    }

    // ---- Format / Insert commands ----
    private void CmdBold_Click(object s, RoutedEventArgs e)       => PostFormat(new JsonObject { ["command"] = "bold" });
    private void CmdItalic_Click(object s, RoutedEventArgs e)     => PostFormat(new JsonObject { ["command"] = "italic" });
    private void CmdStrike_Click(object s, RoutedEventArgs e)     => PostFormat(new JsonObject { ["command"] = "strikethrough" });
    private void CmdInlineCode_Click(object s, RoutedEventArgs e) => PostFormat(new JsonObject { ["command"] = "inlineCode" });
    private void CmdH1_Click(object s, RoutedEventArgs e) => PostFormat(new JsonObject { ["command"] = "heading", ["level"] = 1 });
    private void CmdH2_Click(object s, RoutedEventArgs e) => PostFormat(new JsonObject { ["command"] = "heading", ["level"] = 2 });
    private void CmdH3_Click(object s, RoutedEventArgs e) => PostFormat(new JsonObject { ["command"] = "heading", ["level"] = 3 });
    private void CmdH4_Click(object s, RoutedEventArgs e) => PostFormat(new JsonObject { ["command"] = "heading", ["level"] = 4 });
    private void CmdH5_Click(object s, RoutedEventArgs e) => PostFormat(new JsonObject { ["command"] = "heading", ["level"] = 5 });
    private void CmdH6_Click(object s, RoutedEventArgs e) => PostFormat(new JsonObject { ["command"] = "heading", ["level"] = 6 });
    private void CmdBullet_Click(object s, RoutedEventArgs e)   => PostFormat(new JsonObject { ["command"] = "bulletList" });
    private void CmdNumbered_Click(object s, RoutedEventArgs e) => PostFormat(new JsonObject { ["command"] = "numberedList" });
    private void CmdTask_Click(object s, RoutedEventArgs e)     => PostFormat(new JsonObject { ["command"] = "taskList" });
    private void CmdQuote_Click(object s, RoutedEventArgs e)    => PostFormat(new JsonObject { ["command"] = "blockquote" });
    private void CmdHr_Click(object s, RoutedEventArgs e)       => PostFormat(new JsonObject { ["command"] = "hr" });
    private void CmdClear_Click(object s, RoutedEventArgs e)    => PostFormat(new JsonObject { ["command"] = "clearFormat" });
    private void CmdCodeBlock_Click(object s, RoutedEventArgs e)=> PostFormat(new JsonObject { ["command"] = "codeBlock", ["lang"] = "" });

    private async void CmdLink_Click(object s, RoutedEventArgs e)
    {
        var url = await PromptAsync("Insert Link", "URL:");
        if (url == null) return;
        PostFormat(new JsonObject { ["command"] = "link", ["url"] = url });
    }

    private async void CmdImage_Click(object s, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".png"); picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg"); picker.FileTypeFilter.Add(".gif");
        picker.FileTypeFilter.Add(".svg"); picker.FileTypeFilter.Add(".webp");
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        var path = file.Path;
        var docPath = _activeTab?.FilePath;
        if (!string.IsNullOrEmpty(docPath))
        {
            var docDir = Path.GetDirectoryName(docPath);
            if (!string.IsNullOrEmpty(docDir) && path.StartsWith(docDir, StringComparison.OrdinalIgnoreCase))
                path = Path.GetRelativePath(docDir, path).Replace('\\', '/');
        }
        PostFormat(new JsonObject { ["command"] = "image", ["path"] = path, ["alt"] = Path.GetFileNameWithoutExtension(file.Name) });
    }

    private async void CmdTable_Click(object s, RoutedEventArgs e)
    {
        var rows = new NumberBox { Header = "Rows", Value = 3, Minimum = 1, Maximum = 50, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var cols = new NumberBox { Header = "Cols", Value = 3, Minimum = 1, Maximum = 20, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(rows); stack.Children.Add(cols);
        var dlg = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Insert Table",
            Content = stack,
            PrimaryButtonText = "Insert",
            CloseButtonText = "Cancel"
        };
        var r = await dlg.ShowAsync();
        if (r != ContentDialogResult.Primary) return;
        PostFormat(new JsonObject { ["command"] = "table", ["rows"] = (int)rows.Value, ["cols"] = (int)cols.Value });
    }

    private async Task<string?> PromptAsync(string title, string label)
    {
        var tb = new TextBox { PlaceholderText = label };
        var dlg = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = tb,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        var r = await dlg.ShowAsync();
        return r == ContentDialogResult.Primary ? tb.Text : null;
    }

    private async void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        await ShowErrorAsync("About WordMD", "WordMD (Dr Word) v1.4.0\n\nA friendly, Word-like Markdown editor for Windows.\nThe doctor is in. Markdown made painless.\n\nhttps://github.com/ReboundMan/ReboundMan-WordMD");
    }

    private async void MenuTelemetryToggle_Click(object sender, RoutedEventArgs e)
    {
        var newVal = TelemetryMenuItem.IsChecked;
        if (newVal && !_settings.TelemetryPromptShown)
        {
            var dlg = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Send anonymous usage data?",
                Content = "WordMD can send anonymous usage events (feature counts, no document content, no file paths) to help guide development. " +
                          "Data is stored locally for now; future versions may forward to Microsoft's internal telemetry pipeline. You can turn this off any time.",
                PrimaryButtonText = "Enable",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };
            var r = await dlg.ShowAsync();
            _settings.TelemetryPromptShown = true;
            if (r != ContentDialogResult.Primary)
            {
                TelemetryMenuItem.IsChecked = false;
                _settings.TelemetryOptIn = false;
                _settings.Save();
                return;
            }
        }
        _settings.TelemetryOptIn = newVal;
        _settings.Save();
        _telemetry.TrackEvent(newVal ? "telemetry.enabled" : "telemetry.disabled");
    }

    private void MenuOpenTelemetryFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Path.GetDirectoryName(_telemetry.LogPath);
            if (!string.IsNullOrEmpty(dir))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private async void MenuUserGuide_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "HELP.md");
            if (File.Exists(path))
            {
                await OpenFileInNewTabAsync(path);
            }
            else
            {
                // Fallback: dev tree (running from bin\Debug\... up to repo root).
                var dev = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "HELP.md"));
                if (File.Exists(dev)) await OpenFileInNewTabAsync(dev);
                else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/ReboundMan/ReboundMan-WordMD/blob/main/HELP.md") { UseShellExecute = true });
            }
        }
        catch { }
    }

    private void MenuGitHub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/ReboundMan/ReboundMan-WordMD") { UseShellExecute = true });
        }
        catch { }
    }

    // ---- Feedback / bug reporting ----
    private readonly FeedbackService _feedback = new();

    private async void MenuFeedback_Click(object sender, RoutedEventArgs e)
    {
        var categories = new[] { "bug", "feature", "ux", "performance", "docs", "other" };
        var typeCombo = new ComboBox { Width = 180 };
        foreach (var c in categories) typeCombo.Items.Add(c);
        typeCombo.SelectedIndex = 0;

        var titleBox = new TextBox
        {
            PlaceholderText = "One-line summary",
            Width = 460,
        };
        var descBox = new TextBox
        {
            PlaceholderText = "What happened? What did you expect? Steps to reproduce (for bugs)...",
            AcceptsReturn = true,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            Width = 460,
            Height = 180,
        };
        var diagCheck = new CheckBox { Content = "Include app version, OS, mode, theme, line endings (no document content)", IsChecked = true };
        var openInGitHubCheck = new CheckBox { Content = "Open prefilled GitHub issue after saving locally", IsChecked = true };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Type" });
        panel.Children.Add(typeCombo);
        panel.Children.Add(new TextBlock { Text = "Title" });
        panel.Children.Add(titleBox);
        panel.Children.Add(new TextBlock { Text = "Description" });
        panel.Children.Add(descBox);
        panel.Children.Add(diagCheck);
        panel.Children.Add(openInGitHubCheck);

        var dlg = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Send Feedback",
            Content = panel,
            PrimaryButtonText = "Submit",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        var r = await dlg.ShowAsync();
        if (r != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(titleBox.Text))
        {
            await ShowErrorAsync("Feedback not sent", "Please provide a short title.");
            return;
        }

        var sub = new FeedbackService.Submission
        {
            Category = (string)typeCombo.SelectedItem,
            Title = titleBox.Text.Trim(),
            Description = descBox.Text?.Trim() ?? "",
            IncludeDiagnostics = diagCheck.IsChecked == true,
            Diagnostics = CollectDiagnostics(),
        };
        _feedback.AppendLocal(sub);
        _telemetry.TrackEvent("feedback.submit", new() { ["category"] = sub.Category });

        if (openInGitHubCheck.IsChecked == true)
        {
            try
            {
                var url = _feedback.BuildGitHubIssueUrl(sub);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("Could not open GitHub", ex.Message + "\n\nYour feedback was saved locally to:\n" + _feedback.LogPath);
            }
        }
    }

    private Dictionary<string, string> CollectDiagnostics()
    {
        var d = new Dictionary<string, string>
        {
            ["app.version"] = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown",
            ["os"] = Environment.OSVersion.ToString(),
            ["arch"] = Environment.Is64BitOperatingSystem ? "x64" : "x86",
            ["mode"] = _mode,
            ["theme"] = _theme,
            ["zoom"] = _zoom.ToString("0.00"),
            ["tabs.open"] = _tabs.Count.ToString(),
        };
        if (_activeTab != null)
        {
            d["active.lineEnding"] = _activeTab.LineEnding == "\r\n" ? "CRLF" : "LF";
            d["active.encoding"] = _activeTab.Encoding;
            d["active.hasFile"] = (_activeTab.FilePath != null).ToString();
        }
        return d;
    }

    private void MenuOpenFeedbackFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(FeedbackService.LogDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{FeedbackService.LogDir}\"") { UseShellExecute = true });
        }
        catch { }
    }

    // ---- External-change watching + manual reload ----
    private void AttachWatcher(TabModel tab)
    {
        DetachWatcher(tab);
        if (string.IsNullOrEmpty(tab.FilePath)) return;
        var dir = Path.GetDirectoryName(tab.FilePath);
        var name = Path.GetFileName(tab.FilePath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return;
        try
        {
            var fi = new FileInfo(tab.FilePath);
            tab.LastWriteTimeUtc = fi.LastWriteTimeUtc;
            tab.LastSize = fi.Length;
            var w = new FileSystemWatcher(dir, name)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime
            };
            w.Changed += (_, _) => OnExternalFileEvent(tab);
            w.Created += (_, _) => OnExternalFileEvent(tab);
            w.Renamed += (_, _) => OnExternalFileEvent(tab);
            w.EnableRaisingEvents = true;
            tab.Watcher = w;
        }
        catch { /* unreachable path, network share, etc -- ignore silently */ }
    }

    private static void DetachWatcher(TabModel tab)
    {
        if (tab.Watcher == null) return;
        try { tab.Watcher.EnableRaisingEvents = false; tab.Watcher.Dispose(); } catch { }
        tab.Watcher = null;
    }

    private void OnExternalFileEvent(TabModel tab)
    {
        // FileSystemWatcher fires from a thread-pool thread and often fires
        // multiple events for a single save. Marshal to the UI thread and
        // de-dupe by comparing the on-disk write time to what we last knew.
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                // Skip while we're writing this file ourselves -- the watcher
                // will fire one or more Changed events during our save, and we
                // don't want to prompt the user about their own in-progress
                // save (which would offer to "Reload" and discard their edits).
                if (tab.IsSaving) return;
                if (string.IsNullOrEmpty(tab.FilePath) || !File.Exists(tab.FilePath)) return;
                var fi = new FileInfo(tab.FilePath);
                if (fi.LastWriteTimeUtc <= tab.LastWriteTimeUtc.AddMilliseconds(50) && fi.Length == tab.LastSize)
                    return; // nothing actually changed since last load/save

                tab.ExternallyChanged = true;
                SyncTabHeader(tab);

                if (!tab.IsDirty && _settings.AutoReloadOnExternalChange)
                {
                    var ok = await ReloadTabAsync(tab, prompt: false, silentOnFailure: true);
                    if (ok && tab == _activeTab) StatusFile.Text = tab.DisplayName + "  (reloaded)";
                }
                else if (tab == _activeTab)
                {
                    await PromptExternalChangeAsync(tab);
                }
            }
            catch { }
        });
    }

    private async Task PromptExternalChangeAsync(TabModel tab)
    {
        var dirty = tab.IsDirty;
        var dlg = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "File changed on disk",
            Content = dirty
                ? $"\"{tab.DisplayName}\" was modified outside WordMD, but you have unsaved changes here. Reload from disk and lose your edits?"
                : $"\"{tab.DisplayName}\" was modified outside WordMD. Reload it?",
            PrimaryButtonText = "Reload",
            CloseButtonText = dirty ? "Keep my version" : "Ignore",
        };
        var r = await dlg.ShowAsync();
        if (r == ContentDialogResult.Primary)
        {
            await ReloadTabAsync(tab, prompt: false);
        }
        else
        {
            // User chose to keep their version: clear the indicator but remember
            // the new on-disk timestamp so we don't re-prompt on every keystroke
            // the external editor makes.
            try
            {
                var fi = new FileInfo(tab.FilePath!);
                tab.LastWriteTimeUtc = fi.LastWriteTimeUtc;
                tab.LastSize = fi.Length;
            }
            catch { }
            tab.ExternallyChanged = false;
            SyncTabHeader(tab);
        }
    }

    /// <summary>Re-read the file from disk into the given tab. If prompt=true and the
    /// tab is dirty, asks the user first. If silentOnFailure=true (used by the file
    /// watcher), read failures don't pop an error dialog — the tab is just left
    /// flagged as externally changed so the user can retry manually.</summary>
    private async Task<bool> ReloadTabAsync(TabModel tab, bool prompt, bool silentOnFailure = false)
    {
        if (string.IsNullOrEmpty(tab.FilePath))
        {
            if (tab == _activeTab) StatusFile.Text = tab.DisplayName + "  (no file to reload)";
            return false;
        }
        if (!File.Exists(tab.FilePath))
        {
            if (!silentOnFailure)
                await ShowErrorAsync("Reload failed", $"File not found:\n{tab.FilePath}");
            return false;
        }
        if (prompt && tab.IsDirty)
        {
            var dlg = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Discard unsaved changes?",
                Content = $"Reloading \"{tab.DisplayName}\" from disk will discard your unsaved edits.",
                PrimaryButtonText = "Reload",
                CloseButtonText = "Cancel",
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return false;
        }
        try
        {
            var bytes = await ReadFileBytesWithRetryAsync(tab.FilePath);
            var (text, encoding) = DecodeBytes(bytes);
            var lineEnding = text.Contains("\r\n") ? "\r\n" : "\n";
            tab.LineEnding = lineEnding;
            tab.Encoding = encoding;
            tab.IsDirty = false;
            tab.ExternallyChanged = false;
            var fi = new FileInfo(tab.FilePath);
            tab.LastWriteTimeUtc = fi.LastWriteTimeUtc;
            tab.LastSize = fi.Length;
            // Replace the JS-side doc content for the same docId.
            Post("createDocument", new JsonObject { ["docId"] = tab.DocId, ["text"] = text, ["lineEnding"] = lineEnding });
            if (tab == _activeTab) Post("switchDocument", new JsonObject { ["docId"] = tab.DocId });
            SyncTabHeader(tab);
            if (tab == _activeTab)
            {
                StatusFile.Text = tab.DisplayName;
                StatusEol.Text = lineEnding == "\r\n" ? "CRLF" : "LF";
                StatusEnc.Text = encoding;
                UpdateTitle();
            }
            _telemetry.TrackEvent("file.reload");
            return true;
        }
        catch (Exception ex)
        {
            if (silentOnFailure)
            {
                // Watcher-triggered reload couldn't read the file (most often
                // because the writing app still has it locked). Leave the tab
                // flagged as externally changed so the user can reload manually
                // once the writer is done, and surface a non-blocking hint in
                // the status bar instead of a modal error.
                tab.ExternallyChanged = true;
                SyncTabHeader(tab);
                if (tab == _activeTab)
                    StatusFile.Text = tab.DisplayName + "  (changed on disk — reload to refresh)";
                return false;
            }
            await ShowErrorAsync("Reload failed", ex.Message);
            return false;
        }
    }

    /// <summary>Read a file's bytes, tolerating concurrent writers and brief
    /// sharing violations that happen when another app saves the same file.
    /// Opens with FileShare.ReadWrite|Delete so a writer that still has the
    /// file open doesn't block us, and retries IOExceptions with backoff so
    /// FileSystemWatcher events that fire mid-write succeed once the writer
    /// closes its handle.</summary>
    private static async Task<byte[]> ReadFileBytesWithRetryAsync(string path)
    {
        // ~1.5s total: 50, 75, 110, 165, 245, 365, 545ms.
        int delayMs = 50;
        const int maxAttempts = 8;
        Exception? last = null;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                using var fs = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096,
                    useAsync: true);
                using var ms = new MemoryStream(capacity: (int)Math.Min(fs.Length, int.MaxValue));
                await fs.CopyToAsync(ms);
                return ms.ToArray();
            }
            catch (IOException ex)
            {
                // Sharing violation, file temporarily missing during rename, etc.
                last = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                // Some editors briefly strip ACLs during atomic save (write +
                // rename). Treat as transient.
                last = ex;
            }
            await Task.Delay(delayMs);
            delayMs = (int)(delayMs * 1.5);
        }
        throw last ?? new IOException($"Unable to read file: {path}");
    }

    private async void MenuReload_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab == null) return;
        await ReloadTabAsync(_activeTab, prompt: true);
    }

    private void MenuAutoReload_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoReloadOnExternalChange = AutoReloadMenuItem.IsChecked;
        _settings.Save();
    }

    // ---- Autosave / recovery ----
    private void StartAutosaveTimer()
    {
        _autosaveTimer = new System.Threading.Timer(async _ =>
        {
            try
            {
                if (_activeTab == null || !_activeTab.IsDirty || !_editorReady) return;
                var text = await RequestDocumentTextAsync();
                _recovery.Save(_activeTab.FilePath ?? "untitled-" + _activeTab.DocId, text);
            }
            catch { }
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private async Task MaybeRestoreRecoveryAsync()
    {
        var rec = _recovery.LoadAny();
        if (rec == null) return;
        var dlg = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Recover unsaved work?",
            Content = $"WordMD found unsaved work from a previous session for:\n\n{rec.OriginalPath}\n\nRestore?",
            PrimaryButtonText = "Restore",
            CloseButtonText = "Discard"
        };
        var r = await dlg.ShowAsync();
        if (r == ContentDialogResult.Primary)
        {
            var lineEnding = rec.Text.Contains("\r\n") ? "\r\n" : "\n";
            var path = !rec.OriginalPath.StartsWith("untitled") ? rec.OriginalPath : null;
            var tab = new TabModel
            {
                FilePath = path,
                DisplayName = path != null ? Path.GetFileName(path) : "Recovered.md",
                LineEnding = lineEnding,
                IsDirty = true,
            };
            AddTab(tab, rec.Text, lineEnding);
            SyncTabHeader(tab);
            UpdateTitle();
        }
        _recovery.Clear();
    }

    // ---- Single-instance external activation ----

    // Called when another WordMD process redirects a file-open activation
    // to us (e.g., the user double-clicked a .md while WordMD was already
    // running). Always invoked on the UI thread.
    //
    // Opens the file as a new tab, or focuses an existing tab if the file
    // is already open. Queues the request if the editor isn't ready yet.
    public async Task OpenFileFromExternalAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        if (!_editorReady)
        {
            _pendingExternalFiles.Add(path);
            return;
        }

        // If the file is already open, just switch to that tab.
        var existing = _tabs.FirstOrDefault(t =>
            !string.IsNullOrEmpty(t.FilePath) &&
            string.Equals(Path.GetFullPath(t.FilePath!), TryGetFullPath(path),
                StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            _suppressTabSelection = true;
            DocTabs.SelectedItem = existing.View;
            _suppressTabSelection = false;
            SwitchToTab(existing);
            return;
        }

        await OpenFileInNewTabAsync(path);
    }

    private static string TryGetFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    // Restore the window if minimized and pull it to the foreground so the
    // user sees the just-opened file. Win32 because AppWindow on WinUI 3
    // doesn't currently expose a "force foreground" call.
    public void BringToForeground()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero) return;

            if (NativeMethods.IsIconic(hwnd))
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            else
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);

            NativeMethods.SetForegroundWindow(hwnd);
        }
        catch
        {
            // Foreground stealing protection or window-handle issues are
            // non-fatal — the file will still be opened.
        }
    }

    private static class NativeMethods
    {
        public const int SW_SHOW = 5;
        public const int SW_RESTORE = 9;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);
    }
}
