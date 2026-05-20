using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace WordMD;

/// <summary>
/// Minimal opt-in telemetry. v1 writes a local JSONL log only -- a stable
/// hook surface for wiring 1DS / Aria in v1.1 without touching call sites.
/// Default is OFF. Enable via Help -> Send Anonymous Usage Data.
/// </summary>
public class TelemetryService
{
    private readonly SettingsStore _settings;
    private readonly string _logPath;
    private readonly object _lock = new();
    private readonly string _sessionId = Guid.NewGuid().ToString("N").Substring(0, 12);

    public TelemetryService(SettingsStore settings)
    {
        _settings = settings;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WordMD", "telemetry");
        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, $"events-{DateTime.UtcNow:yyyyMMdd}.jsonl");
    }

    public bool Enabled => _settings.TelemetryOptIn;

    public void TrackEvent(string name, Dictionary<string, object?>? props = null)
    {
        if (!Enabled) return;
        try
        {
            var entry = new JsonObject
            {
                ["ts"]      = DateTime.UtcNow.ToString("O"),
                ["session"] = _sessionId,
                ["name"]    = name,
            };
            var propsNode = new JsonObject();
            if (props != null)
            {
                foreach (var kv in props) propsNode[kv.Key] = ToJsonValue(kv.Value);
            }
            entry["props"] = propsNode;

            var json = entry.ToJsonString();
            lock (_lock)
            {
                File.AppendAllText(_logPath, json + Environment.NewLine);
            }
        }
        catch { /* never throw from telemetry */ }
    }

    // Trim-safe conversion for telemetry property values. Avoids JsonValue.Create<T>'s
    // reflection fallback for unknown T by branching on common runtime types.
    private static JsonNode? ToJsonValue(object? v) => v switch
    {
        null         => null,
        string s     => s,
        bool b       => b,
        int i        => i,
        long l       => l,
        short sh     => (int)sh,
        byte by      => (int)by,
        double d     => d,
        float f      => f,
        decimal m    => m,
        DateTime dt  => dt.ToString("O"),
        Guid g       => g.ToString(),
        _            => v.ToString(),
    };

    public string LogPath => _logPath;
}
