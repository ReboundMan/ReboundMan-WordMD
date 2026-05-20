using System.Text.Json.Serialization;

namespace WordMD;

/// <summary>
/// Source-generated JSON serialization for trim-safe Release builds.
///
/// System.Text.Json's reflection-based path is incompatible with
/// PublishTrimmed=True (it strips the metadata STJ needs). This partial
/// class lets the source generator emit a JsonTypeInfo for every type we
/// serialize, so JsonSerializer.Serialize(value, ctx.SomeType) works
/// without reflection.
///
/// Anonymous-typed payloads (used in MainWindow.Post, FeedbackService,
/// TelemetryService) cannot be source-generated and are built explicitly
/// with System.Text.Json.Nodes.JsonObject instead.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SettingsStore))]
internal partial class JsonContext : JsonSerializerContext { }
