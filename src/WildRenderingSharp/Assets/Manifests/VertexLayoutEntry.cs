using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets.Manifests;

/// <summary>
/// One entry of <c>vertex_layout</c>: an attribute's fixed byte offset/component count in <c>ExportTestBench</c>'s 192-byte
/// interleaved vertex.
/// </summary>
public sealed class VertexLayoutEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("location")] public int Location { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
    [JsonPropertyName("components")] public int Components { get; set; }
}
