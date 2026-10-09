using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets.Manifests;

/// <summary>Which compiled program index (or -1, unresolved) each pipeline stage selected for a shape's material.</summary>
internal sealed class ProgramIndices
{
    [JsonPropertyName("gbuffer")] public int GBuffer { get; set; } = -1;
    [JsonPropertyName("zonly")] public int ZOnly { get; set; } = -1;
    [JsonPropertyName("material")] public int Material { get; set; } = -1;
}
