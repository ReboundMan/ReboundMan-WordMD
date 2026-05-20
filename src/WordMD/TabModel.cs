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
