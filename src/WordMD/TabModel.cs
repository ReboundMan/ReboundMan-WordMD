using System;
using System.IO;
using Microsoft.UI.Xaml.Controls;

namespace WordMD;

/// <summary>One open document tab.</summary>
public sealed class TabModel
{
    public string DocId { get; } = Guid.NewGuid().ToString("N").Substring(0, 12);
    public string? FilePath { get; set; }
    public string DisplayName { get; set; } = "Untitled.md";
    public bool IsDirty { get; set; }
    /// <summary>True while a host-initiated save is writing this tab's file.
    /// The external-change watcher checks this to avoid prompting the user
    /// about their own in-progress save. Also serves as a reentrancy guard
    /// preventing overlapping Save invocations (e.g. Ctrl+S repeat).</summary>
    public bool IsSaving { get; set; }

    /// <summary>Coalescing state for OnExternalFileEvent: when true, a handler
    /// is currently executing for this tab. Subsequent watcher events should
    /// flip <see cref="ExternalEventPending"/> instead of starting a parallel
    /// handler. Read/written only on the UI thread.</summary>
    public bool ExternalEventRunning { get; set; }

    /// <summary>Coalescing state for OnExternalFileEvent: set by watcher events
    /// that arrived while a handler was already in flight. The active handler
    /// re-runs its evaluation once before exiting when this is true. Read/written
    /// only on the UI thread.</summary>
    public bool ExternalEventPending { get; set; }
    public string LineEnding { get; set; } = "\r\n";
    public string Encoding { get; set; } = "UTF-8";

    /// <summary>The TabViewItem in the UI -- set when the tab is bound.</summary>
    public TabViewItem? View { get; set; }

    // ---- External-change watcher state ----
    public FileSystemWatcher? Watcher { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }
    public long LastSize { get; set; }
    /// <summary>True when the file on disk has changed since we last loaded/saved it.</summary>
    public bool ExternallyChanged { get; set; }

    public string TabHeader =>
        (IsDirty ? "● " : ExternallyChanged ? "⟳ " : "") + DisplayName;
}
