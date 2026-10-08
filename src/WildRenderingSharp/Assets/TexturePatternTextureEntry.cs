using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>
/// One texture a pattern anim can select, with everything <see cref="TextureCache"/> needs to load it - the same shape as a <see
/// cref="SamplerBinding"/> minus the unit/key, which belong to the sampler entry that selects it rather than to the texture.
/// </summary>
public sealed class TexturePatternTextureEntry
{
    [JsonPropertyName("texture")] public string Texture { get; set; } = "";
    // File name relative to the data directory, or empty if the texture failed to export - the entry is still listed so it can
    // be logged rather than silently dropped.
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("format")] public string Format { get; set; } = "";
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
}
