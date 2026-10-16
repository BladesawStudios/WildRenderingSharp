using System.Text.Json.Serialization;

namespace WildRenderingSharp.Animation.Clips;

/// <summary>
/// Metadata for a texture selectable by a texture pattern animation.
/// </summary>
public sealed class TexturePatternTextureEntry
{
    [JsonPropertyName("texture")] public string Texture { get; set; } = "";
    // File name relative to the data directory, or empty if the texture failed to export
    [JsonPropertyName("file")] public string File { get; set; } = "";
    // TODO: Make this an enum when we clean up the texture loading system.
    [JsonPropertyName("format")] public string Format { get; set; } = "";
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
}
