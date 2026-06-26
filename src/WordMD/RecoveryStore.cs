using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace WordMD;

public sealed class RecoveryRecord
{
    public string OriginalPath { get; set; } = "";
    public string Text { get; set; } = "";
    public string Encoding { get; set; } = "UTF-8";
    public DateTime Timestamp { get; set; }
}

public sealed class RecoveryStore
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WordMD", "recovery");

    public bool HasRecovery() => Directory.Exists(Dir) && Directory.GetFiles(Dir, "*.recover").Any();

    public void Save(string originalPath, string text, string encoding = "UTF-8")
    {
        Directory.CreateDirectory(Dir);
        var safe = SafeName(originalPath);
        var bodyPath = Path.Combine(Dir, safe + ".md");
        var metaPath = Path.Combine(Dir, safe + ".recover");
        // Body first, meta second: a crash in between leaves an orphan .md
        // (harmless — restore keys off .recover) rather than a meta pointing at
        // a body that doesn't exist. Meta is "path\nencoding" so a recovered
        // file round-trips its original encoding (e.g. a UTF-8 BOM).
        WriteAtomic(bodyPath, text);
        WriteAtomic(metaPath, originalPath + "\n" + encoding);
    }

    /// <summary>Remove the recovery record for a single document (called when a
    /// tab is saved or closed cleanly) without disturbing other tabs' records.</summary>
    public void Remove(string originalPath)
    {
        if (!Directory.Exists(Dir)) return;
        var safe = SafeName(originalPath);
        try { File.Delete(Path.Combine(Dir, safe + ".md")); } catch { }
        try { File.Delete(Path.Combine(Dir, safe + ".recover")); } catch { }
    }

    /// <summary>All recoverable records (one per dirty tab snapshotted before a crash).</summary>
    public List<RecoveryRecord> LoadAll()
    {
        var list = new List<RecoveryRecord>();
        if (!Directory.Exists(Dir)) return list;
        foreach (var meta in Directory.GetFiles(Dir, "*.recover"))
        {
            try
            {
                var bodyPath = Path.ChangeExtension(meta, ".md");
                if (!File.Exists(bodyPath)) continue;
                var metaLines = File.ReadAllText(meta).Split('\n');
                list.Add(new RecoveryRecord
                {
                    OriginalPath = metaLines[0].TrimEnd('\r'),
                    Encoding = metaLines.Length > 1 ? metaLines[1].TrimEnd('\r') : "UTF-8",
                    Text = File.ReadAllText(bodyPath),
                    Timestamp = File.GetLastWriteTime(bodyPath),
                });
            }
            catch { }
        }
        return list;
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

    private static void WriteAtomic(string path, string contents)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        // Move with overwrite is atomic on the same volume, so a crash never
        // leaves a half-written destination file.
        File.Move(tmp, path, overwrite: true);
    }

    // Deterministic, collision-free key derived from the full path. Hex SHA-256
    // is 64 chars (well within MAX_PATH) and never truncates, so parallel trees
    // that share a suffix no longer clobber each other's recovery snapshot.
    private static string SafeName(string s)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
