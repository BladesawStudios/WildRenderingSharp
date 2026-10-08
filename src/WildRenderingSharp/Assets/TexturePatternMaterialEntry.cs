using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>Every sampler of one material that this anim drives.</summary>
public sealed class TexturePatternMaterialEntry
{
    /// <summary>The material name, matched against <see cref="LoadedShape.Material"/> - one material can back several shapes, and the swap applies to all of them.</summary>
    [JsonPropertyName("material")] public string Material { get; set; } = "";
    [JsonPropertyName("samplers")] public List<TexturePatternSamplerEntry> Samplers { get; set; } = [];
}
