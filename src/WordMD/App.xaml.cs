using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WordMD;

public partial class App : Application
{
    public static MainWindow? MainWindowInstance { get; private set; }
    public static string? StartupFilePath { get; private set; }

    // External-activation paths that arrived before MainWindow existed.
    // Drained once the window is created so we don't lose double-clicks
    // received during startup.
    private static readonly object _pendingExternalLock = new();
    private static readonly List<string> _pendingExternalFiles = new();

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        StartupFilePath = ParseStartupFile(args.Arguments);
        if (string.IsNullOrEmpty(StartupFilePath))
        {
            // Unpackaged WinUI apps often don't get args.Arguments populated.
            // Fall back to the process command line.
            var cli = Environment.GetCommandLineArgs();
            for (int i = 1; i < cli.Length; i++)
            {
                if (System.IO.File.Exists(cli[i])) { StartupFilePath = cli[i]; break; }
            }
        }
        MainWindowInstance = new MainWindow();
        MainWindowInstance.Activate();

        // If a second instance redirected us a file before MainWindow existed,
        // open those now.
        var pending = DrainPendingExternalFiles();
        foreach (var p in pending)
            _ = MainWindowInstance.OpenFileFromExternalAsync(p);
    }

    private static string? ParseStartupFile(string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return null;
        try
        {
            // Strip surrounding quotes from a single path argument.
            var s = cmd.Trim();
            if (s.StartsWith("\"") && s.EndsWith("\"") && s.Length >= 2)
                s = s.Substring(1, s.Length - 2);
            if (System.IO.File.Exists(s)) return s;
        }
        catch { }
        return null;
    }

    // Called by Program.OnRedirectedActivated when a second WordMD process
    // redirects its activation here. Runs on a non-UI thread.
    internal static void OnExternalActivation(string? path)
    {
        MainWindow? win = MainWindowInstance;
        if (win == null)
        {
            // Window not constructed yet (rare race during startup); queue and
            // let OnLaunched drain after creating MainWindow.
            if (!string.IsNullOrEmpty(path))
            {
                lock (_pendingExternalLock) _pendingExternalFiles.Add(path);
            }
            return;
        }

        win.DispatcherQueue.TryEnqueue(() =>
        {
            win.BringToForeground();
            if (!string.IsNullOrEmpty(path))
                _ = win.OpenFileFromExternalAsync(path);
        });
    }

    private static List<string> DrainPendingExternalFiles()
    {
        lock (_pendingExternalLock)
        {
            var copy = _pendingExternalFiles.ToList();
            _pendingExternalFiles.Clear();
            return copy;
        }
    }
}

