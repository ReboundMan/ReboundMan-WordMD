using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace WordMD;

// Help > Check for Updates...: the UI half. The networking, download, and verification
// live in UpdateService (no UI types there, so it can be tested on its own); this file
// only presents dialogs and, once a verified installer exists, closes WordMD and starts it.
// Design and threat model: spec\features\check-for-updates.md.
public sealed partial class MainWindow
{
    private bool _updateFlowActive;

    // async void is forced by the WinUI Click signature, so an exception escaping here
    // would have no caller and would take the whole process down (this is exactly how the
    // About dialog once crashed the app). Nothing in the update flow may escape this method.
    private async void MenuCheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_updateFlowActive) return;
        _updateFlowActive = true;
        try
        {
            await RunUpdateFlowAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MenuCheckForUpdates_Click failed: {ex}");
            try
            {
                await ShowUpdateProblemAsync("Check for Updates",
                    "Something went wrong while checking for updates. Nothing on your computer was changed.", "unexpected");
            }
            catch { }
        }
        finally
        {
            _updateFlowActive = false;
        }
    }

    private async Task RunUpdateFlowAsync()
    {
        _ = Task.Run(UpdateService.CleanStaleDownloads);

        // A development build with a suffix (for example 2.2.0-rc1) can't be compared with
        // a release honestly, and guessing would let it "update" to an older version.
        if (!UpdateService.TryParseReleaseVersion(AppVersion, out var current))
        {
            await ShowUpdateProblemAsync("Check for Updates",
                $"This build of WordMD (v{AppVersion}) doesn't have a standard release version number, so it can't tell whether a newer release exists. You can see the latest release on GitHub.",
                null);
            return;
        }

        using var cts = new CancellationTokenSource();
        var checkTask = Task.Run(() => UpdateService.CheckAsync(current, cts.Token));
        if (!await ShowWorkDialogAsync("Check for Updates", "Checking GitHub for the latest version...", "Checking for updates", checkTask, cts, null, null))
            return;

        var result = await checkTask;
        switch (result.Status)
        {
            case UpdateCheckStatus.UpToDate:
                await ShowInfoAsync("You're up to date",
                    result.Latest != null && result.Latest < current
                        ? $"You're running v{AppVersion}, which is newer than the latest published release (v{result.Latest.ToString(3)})."
                        : $"WordMD v{AppVersion} is the latest version.");
                break;
            case UpdateCheckStatus.Available:
                await OfferUpdateAsync(result.Update!);
                break;
            default:
                await ShowUpdateProblemAsync("Couldn't check for updates", result.Error ?? "Something went wrong.", result.Code);
                break;
        }
    }

    private async Task OfferUpdateAsync(UpdateInfo update)
    {
        // Only the installed copy can meaningfully update itself. A portable or development
        // copy would run the installer against some other install and then close, taking
        // away the copy the user was actually using.
        var canInstall = UpdateService.PathsMatch(AppContext.BaseDirectory, UpdateService.ReadInstallLocation());

        var panel = new StackPanel { Spacing = 10, MinWidth = 380 };
        panel.Children.Add(new TextBlock
        {
            Text = $"You're running v{AppVersion}.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = canInstall
                ? "To install it, WordMD will download the update from GitHub, check it, then close and open the installer. You'll be asked to save any unsaved changes first."
                : "This copy of WordMD isn't the installed one (it may be a portable or development copy), so it can't update itself. You can get the new version from the GitHub releases page.",
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(update.Notes))
        {
            // A read-only TextBox rather than a TextBlock in a ScrollViewer: it is a keyboard
            // tab stop, scrolls with the arrow and page keys, and is exposed to screen readers.
            // Still plain text, so release notes cannot inject markup or links into the dialog.
            var notes = new TextBox
            {
                Header = "What's new",
                Text = update.Notes,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 220,
                IsSpellCheckEnabled = false,
            };
            AutomationProperties.SetName(notes, "What's new, release notes");
            panel.Children.Add(notes);
        }

        var dlg = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = RootGrid.ActualTheme,
            Title = $"WordMD v{update.Version.ToString(3)} is available",
            Content = panel,
        };
        if (canInstall)
        {
            dlg.PrimaryButtonText = "Download and install";
            dlg.SecondaryButtonText = "View release notes";
            dlg.CloseButtonText = "Not now";
            // The safe choice is the default: Enter never starts a download that ends with
            // WordMD closing.
            dlg.DefaultButton = ContentDialogButton.Close;
        }
        else
        {
            dlg.PrimaryButtonText = "Open releases page";
            dlg.CloseButtonText = "Close";
            dlg.DefaultButton = ContentDialogButton.Primary;
        }

        var choice = await dlg.ShowAsync();
        if (!canInstall)
        {
            if (choice == ContentDialogResult.Primary) OpenExternal(update.ReleasePageUrl);
            return;
        }
        if (choice == ContentDialogResult.Secondary)
        {
            OpenExternal(update.ReleasePageUrl);
            return;
        }
        if (choice != ContentDialogResult.Primary) return;

        await DownloadAndInstallAsync(update);
    }

    private async Task DownloadAndInstallAsync(UpdateInfo update)
    {
        using var cts = new CancellationTokenSource();
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0 };
        var status = new TextBlock { Text = "Starting the download...", TextWrapping = TextWrapping.Wrap };
        // Announced politely when it changes, so a screen reader hears progress and the switch
        // to the checking phase without being flooded.
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var lastReported = -10.0;
        // Created on the UI thread, so Report() callbacks are marshalled back to it.
        var progress = new Progress<double>(p =>
        {
            try
            {
                bar.Value = Math.Max(0, Math.Min(100, p));
                if (p >= 100)
                {
                    // The bytes are all here but the checksum and signature checks are still
                    // running; a full bar would read as "done" while the dialog lingers.
                    bar.IsIndeterminate = true;
                    status.Text = "Checking the download...";
                }
                else if (p - lastReported >= 10)
                {
                    lastReported = p;
                    status.Text = $"{(int)p}% ({FormatMegabytes((long)(update.InstallerSize * p / 100))} of {FormatMegabytes(update.InstallerSize)})";
                }
            }
            catch { }
        });
        var downloadTask = Task.Run(() => UpdateService.DownloadAndVerifyAsync(update, progress, cts.Token));

        bool finished;
        try
        {
            finished = await ShowWorkDialogAsync(
                "Downloading update",
                $"Downloading WordMD v{update.Version.ToString(3)} ({FormatMegabytes(update.InstallerSize)})...",
                "Download progress", downloadTask, cts, bar, status);
        }
        catch
        {
            DiscardWhenDone(downloadTask);
            throw;
        }
        if (!finished)
        {
            // Cancelled. If the download happened to finish in the same instant, don't leave a
            // verified installer lying around.
            DiscardWhenDone(downloadTask);
            return;
        }

        VerifiedInstaller installer;
        try
        {
            installer = await downloadTask;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return;
        }
        catch (UpdateException ex)
        {
            var (title, message, code) = DescribeUpdateFailure(ex);
            await ShowUpdateProblemAsync(title, message, code);
            return;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update download failed: {ex}");
            await ShowUpdateProblemAsync("Couldn't download the update",
                "Something went wrong while downloading the update.\n\nNothing on your computer was changed.", "unexpected");
            return;
        }

        try
        {
            // The installer replaces files this process is running from, so WordMD must close.
            // Same unsaved-changes gate as File > Exit; cancelling it abandons the update.
            if (!await ConfirmAllDirtyTabsAsync())
            {
                installer.DisposeAndDelete();
                return;
            }

            if (!await TryLaunchInstallerAsync(installer.FilePath))
            {
                var path = installer.FilePath;
                installer.Dispose();
                await ShowUpdateProblemAsync("Couldn't start the installer",
                    "The update was downloaded and checked, but Windows didn't start the installer. Security software or a policy on this computer may have blocked it. You can run it yourself:\n\n" + path,
                    "launch");
                return;
            }

            // Launched. Deliberately not disposing the installer: its read-only handle keeps
            // the checked file from being swapped until this process exits a moment from now.
            // That closes the gap up to process creation; it is not a guarantee about what the
            // installer re-reads from disk afterwards (the signature is what vouches for it).
            Application.Current.Exit();
        }
        catch
        {
            installer.DisposeAndDelete();
            throw;
        }
    }

    private static void DiscardWhenDone(Task<VerifiedInstaller> task)
    {
        _ = task.ContinueWith(t =>
        {
            if (t.Status == TaskStatus.RanToCompletion) t.Result.DisposeAndDelete();
            else _ = t.Exception;
        }, TaskScheduler.Default);
    }

    private static (string Title, string Message, string Code) DescribeUpdateFailure(UpdateException ex) => ex switch
    {
        UpdateVerificationException => ("Update not installed",
            "The downloaded installer failed a safety check, so WordMD did not run it and deleted it.\n\n" + ex.Message + "\n\nNothing on your computer was changed.",
            ex.Code),
        UpdateSourceException => ("Update not downloaded",
            ex.Message + "\n\nNothing was downloaded or changed.",
            ex.Code),
        _ => ("Couldn't download the update",
            ex.Message + "\n\nNothing on your computer was changed.",
            ex.Code),
    };

    /// <summary>
    /// Starts the installer through the shell, showing its normal window. Deliberately not
    /// silent, so any problem is visible to the user and the finish page's "Launch WordMD"
    /// option can bring the app back. Note that WordMD exits right after this succeeds, so if
    /// the user later cancels Setup (or declines an elevation prompt) the app stays closed;
    /// the only early failure it can catch is Setup dying immediately with an error.
    /// </summary>
    private static async Task<bool> TryLaunchInstallerAsync(string path)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                // Not the installer's own (user-writable) folder, so nothing planted beside it
                // is picked up as the working directory.
                WorkingDirectory = Environment.SystemDirectory,
            });
            try
            {
                // Setup stays alive until the user finishes or cancels it. One that is already
                // gone with a failure code was blocked or crashed, and exiting now would leave
                // the user with neither the app nor an installer.
                if (p != null && await Task.Run(() => p.WaitForExit(1500)) && p.ExitCode != 0) return false;
            }
            catch
            {
                // Can't observe the process; assume it is running.
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Launching the installer failed: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Shows a modal "working" dialog that closes itself when <paramref name="work"/>
    /// completes. Returns false if the user cancelled (after cancelling <paramref name="cts"/>),
    /// true if the work finished. Three details matter:
    /// work that finishes within a moment never shows a dialog at all (no flash, no focus
    /// churn); the dialog is closed from its Opened event, never before, so a task that
    /// finishes before the dialog is on screen cannot leave a dialog nothing will dismiss;
    /// and "cancelled" is decided by what actually closed the dialog, not by whether the task
    /// happened to be finished by the time the dialog finished closing.
    /// </summary>
    private async Task<bool> ShowWorkDialogAsync(
        string title, string message, string automationName, Task work, CancellationTokenSource cts, ProgressBar? bar, TextBlock? status)
    {
        if (await Task.WhenAny(work, Task.Delay(400)) == work) return true;

        var panel = new StackPanel { Spacing = 12, MinWidth = 380 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var indicator = bar ?? new ProgressBar { IsIndeterminate = true };
        AutomationProperties.SetName(indicator, automationName);
        panel.Children.Add(indicator);
        if (status != null) panel.Children.Add(status);

        var cancelClicked = false;
        var closedByWork = false;
        var dlg = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = RootGrid.ActualTheme,
            Title = title,
            Content = panel,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        dlg.CloseButtonClick += (_, _) => cancelClicked = true;
        dlg.Opened += async (_, _) =>
        {
            try
            {
                try { await work; } catch { }
                closedByWork = true;
                dlg.Hide();
            }
            catch { }
        };

        try
        {
            await dlg.ShowAsync();
        }
        catch
        {
            // Another dialog was already open, or the window is going away: the work is
            // already running, so stop it rather than let a download carry on unseen.
            cts.Cancel();
            throw;
        }

        if (closedByWork && !cancelClicked) return true;
        cts.Cancel();
        return false;
    }

    private async Task ShowInfoAsync(string title, string message)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = RootGrid.ActualTheme,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "OK",
        };
        await dlg.ShowAsync();
    }

    /// <summary>
    /// An error dialog that also offers the manual route: the releases page. The message is
    /// expected to be one of the service's fixed sentences; the length cap is only a backstop,
    /// and the short code is for support, not for the user to interpret.
    /// </summary>
    private async Task ShowUpdateProblemAsync(string title, string message, string? code)
    {
        var text = message.Length > 600 ? message.Substring(0, 600) + "..." : message;
        if (!string.IsNullOrEmpty(code)) text += $"\n\nError code: {code}";
        var dlg = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = RootGrid.ActualTheme,
            Title = title,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            PrimaryButtonText = "Open releases page",
            CloseButtonText = "Close",
            // Enter dismisses; opening a browser is a deliberate choice.
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            OpenExternal(SupportLinks.ReleasesPage);
    }

    private static string FormatMegabytes(long bytes) => $"{bytes / 1048576.0:0.#} MB";
}
