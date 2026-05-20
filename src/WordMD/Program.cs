using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace WordMD;

// Custom Program.Main so WordMD can be a "single-instance" app: when the user
// double-clicks a second .md file, the running instance opens it as a new tab
// instead of spawning a second process. Uses the Windows App SDK
// AppInstance.FindOrRegisterForKey + RedirectActivationToAsync pattern.
public static class Program
{
    // Per-user key that identifies the canonical WordMD process for redirection.
    private const string SingleInstanceKey = "WordMD-SingleInstance-{c4f1c5b0-9c66-4a31-9c1d-3a8a4b6e2f10}";

    [STAThread]
    [System.Diagnostics.CodeAnalysis.DynamicDependency(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All, typeof(App))]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (DecideRedirection())
        {
            // Activation was handed off to the existing instance; this process
            // should exit immediately so Explorer doesn't see a second window.
            return 0;
        }

        Application.Start((p) =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    // Returns true if this process redirected activation to an existing
    // instance and should now exit.
    private static bool DecideRedirection()
    {
        var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        var keyInstance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);

        if (keyInstance.IsCurrent)
        {
            // We are the canonical instance. Listen for subsequent activations
            // forwarded by other processes that attempt to launch WordMD.
            keyInstance.Activated += OnRedirectedActivated;
            return false;
        }

        // Give the primary process permission to come to the foreground when
        // it handles our redirected activation. Without this, foreground-
        // stealing protection often leaves the WordMD window behind Explorer.
        try { AllowSetForegroundWindow((int)keyInstance.ProcessId); } catch { }

        try
        {
            keyInstance.RedirectActivationToAsync(activatedArgs).AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            // Primary instance is hung, crashed, or otherwise unreachable.
            // Falling through to start a normal window is better than dropping
            // the file the user asked us to open — they may briefly see two
            // WordMD windows, but their file will appear.
            return false;
        }
    }

    private static void OnRedirectedActivated(object? sender, AppActivationArguments args)
    {
        // Runs on a background thread; App.OnExternalActivation marshals to UI.
        var path = ExtractFilePathFromActivation(args);
        App.OnExternalActivation(path);
    }

    // Pulls a file path out of either a File-kind activation (packaged scenarios)
    // or a Launch-kind activation (unpackaged, which is how WordMD ships today).
    //
    // NOTE: We intentionally do NOT fall back to Environment.GetCommandLineArgs()
    // here. This method runs in the *primary* instance when handling a
    // redirected activation, so the primary's command line is unrelated to the
    // file the secondary process was launched with.
    internal static string? ExtractFilePathFromActivation(AppActivationArguments args)
    {
        try
        {
            if (args.Kind == ExtendedActivationKind.File &&
                args.Data is IFileActivatedEventArgs fileArgs)
            {
                var first = fileArgs.Files?.OfType<Windows.Storage.IStorageItem>().FirstOrDefault();
                if (first != null && File.Exists(first.Path)) return first.Path;
            }

            if (args.Kind == ExtendedActivationKind.Launch &&
                args.Data is ILaunchActivatedEventArgs launchArgs)
            {
                var fromArgs = ParseFirstExistingPath(launchArgs.Arguments);
                if (fromArgs != null) return fromArgs;
            }
        }
        catch
        {
            // Fall through; no path available.
        }

        return null;
    }

    internal static string? ParseFirstExistingPath(string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return null;
        try
        {
            // Common single-argument case: a quoted or bare path.
            var trimmed = cmd.Trim();
            if (trimmed.Length >= 2 && trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
                trimmed = trimmed.Substring(1, trimmed.Length - 2);
            if (File.Exists(trimmed)) return trimmed;

            foreach (var token in SplitCommandLine(cmd))
            {
                if (File.Exists(token)) return token;
            }
        }
        catch { }
        return null;
    }

    private static IEnumerable<string> SplitCommandLine(string cmd)
    {
        var sb = new StringBuilder();
        bool inQuotes = false;
        foreach (var ch in cmd)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (ch == ' ' && !inQuotes)
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                continue;
            }
            sb.Append(ch);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}
