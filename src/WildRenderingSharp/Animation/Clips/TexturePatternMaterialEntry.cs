using System.Text.Json.Serialization;

namespace WildRenderingSharp.Animation.Clips;

/// <summary>Every sampler of one material that this anim drives.</summary>
public sealed class TexturePatternMaterialEntry
{
    // The material name, matched against Material - one material can back several shapes, and the swap applies to all of them.
    [JsonPropertyName("material")] public string Material { get; set; } = "";
    [JsonPropertyName("samplers")] public List<TexturePatternSamplerEntry> Samplers { get; set; } = [];
}
