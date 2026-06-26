using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WordMD;

public sealed class SettingsStore
{
    public List<string> RecentFiles { get; set; } = new();
    public string Mode { get; set; } = "split";
    public string Theme { get; set; } = "light";
    public double Zoom { get; set; } = 1.0;
    public bool TelemetryOptIn { get; set; } = false;
    public bool TelemetryPromptShown { get; set; } = false;
    public bool ScrollSync { get; set; } = true;
    public bool LockToSource { get; set; } = true;
    /// <summary>When a file is modified outside WordMD and the tab is clean, auto-reload it.</summary>
    public bool AutoReloadOnExternalChange { get; set; } = true;

    /// <summary>
    /// Per-file last-used mode (file path -> "source" / "formatted" / "split").
    /// On open, if a file is in this dict, its remembered mode wins over the
    /// global default. Capped at the same size as RecentFiles to avoid bloat.
    /// </summary>
    public Dictionary<string, string> DocModes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WordMD");
    private static string Path_ => System.IO.Path.Combine(Dir, "settings.json");

    public void Load()
    {
        try
        {
            if (!File.Exists(Path_)) return;
            var json = File.ReadAllText(Path_);
            var loaded = JsonSerializer.Deserialize(json, JsonContext.Default.SettingsStore);
            if (loaded == null) return;
            RecentFiles = loaded.RecentFiles ?? new();
            Mode = loaded.Mode ?? "split";
            Theme = loaded.Theme ?? "light";
            Zoom = loaded.Zoom > 0 ? loaded.Zoom : 1.0;
            TelemetryOptIn = loaded.TelemetryOptIn;
            TelemetryPromptShown = loaded.TelemetryPromptShown;
            ScrollSync = loaded.ScrollSync;
            LockToSource = loaded.LockToSource;
            AutoReloadOnExternalChange = loaded.AutoReloadOnExternalChange;
            DocModes = loaded.DocModes != null
                ? new Dictionary<string, string>(loaded.DocModes, StringComparer.OrdinalIgnoreCase)
                : new(StringComparer.OrdinalIgnoreCase);
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(this, JsonContext.Default.SettingsStore);
            // Atomic write: a crash mid-write must not truncate settings.json and
            // silently reset recents/theme/telemetry on the next launch.
            var tmp = Path_ + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, Path_, overwrite: true);
        }
        catch { }
    }

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 10) RecentFiles = RecentFiles.Take(10).ToList();
        // Trim DocModes opportunistically to avoid unbounded growth.
        if (DocModes.Count > 200)
        {
            var keep = new HashSet<string>(RecentFiles, StringComparer.OrdinalIgnoreCase);
            foreach (var k in DocModes.Keys.ToList())
                if (!keep.Contains(k)) DocModes.Remove(k);
        }
    }

    /// <summary>Remember the mode the user last left a given file in.</summary>
    public void RememberDocMode(string path, string mode)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(mode)) return;
        DocModes[path] = mode;
    }

    public string? GetRememberedDocMode(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        return DocModes.TryGetValue(path, out var m) ? m : null;
    }
}
