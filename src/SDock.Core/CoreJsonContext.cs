using System.Text.Json.Serialization;

namespace SDock.Core;

/// <summary>Source-generated JSON (works with trimming and ahead-of-time compilation).</summary>
[JsonSerializable(typeof(List<string>))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
