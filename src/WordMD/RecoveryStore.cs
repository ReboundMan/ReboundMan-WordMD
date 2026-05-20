using System;
using System.IO;
using System.Linq;

namespace WordMD;

public sealed class RecoveryRecord
{
    public string OriginalPath { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

public sealed class RecoveryStore
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WordMD", "recovery");

    public bool HasRecovery() => Directory.Exists(Dir) && Directory.GetFiles(Dir, "*.recover").Any();

    public void Save(string originalPath, string text)
    {
        Directory.CreateDirectory(Dir);
        var safe = SafeName(originalPath);
        var meta = Path.Combine(Dir, safe + ".recover");
        File.WriteAllText(meta, originalPath);
        File.WriteAllText(Path.Combine(Dir, safe + ".md"), text);
    }

    public RecoveryRecord? LoadAny()
    {
        if (!Directory.Exists(Dir)) return null;
        var meta = Directory.GetFiles(Dir, "*.recover").FirstOrDefault();
        if (meta == null) return null;
        var orig = File.ReadAllText(meta);
        var bodyPath = Path.ChangeExtension(meta, ".md");
        if (!File.Exists(bodyPath)) return null;
        return new RecoveryRecord
        {
            OriginalPath = orig,
            Text = File.ReadAllText(bodyPath),
            Timestamp = File.GetLastWriteTime(bodyPath)
        };
    }

    public void Clear()
    {
        if (!Directory.Exists(Dir)) return;
        try
        {
            foreach (var f in Directory.GetFiles(Dir)) File.Delete(f);
        }
        catch { }
    }

    private static string SafeName(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var arr = s.Select(c => bad.Contains(c) ? '_' : c).ToArray();
        var n = new string(arr);
        if (n.Length > 80) n = n.Substring(n.Length - 80);
        return n;
    }
}
