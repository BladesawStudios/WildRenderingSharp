using System.Text.Json;
using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>
/// Deserialized <c>&lt;Model&gt;.&lt;AnimName&gt;.texpat.json</c>, written by
/// <c>ShaderLibrary.CompileTool.ExportTexturePatternAnim</c> from a BFRES texture pattern anim (an FMAA with <c>TexturePatternCount
/// &gt; 0</c>). A texture pattern anim does not move or shade anything - it re-points a material's SAMPLER at a different texture
/// per frame. See <c>ExportTexturePatternAnim</c> for the Ghidra citations behind that; <see
/// cref="WildRenderingSharp.Rendering.TexturePatternPose"/> is the runtime that applies it.
/// </summary>
public sealed class TexturePatternAnimManifest : IAnimClip
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("frame_count")] public int FrameCount { get; set; }
    [JsonPropertyName("loop")] public bool Loop { get; set; }
    /// <summary>The anim's own texture list - <see cref="TexturePatternSamplerEntry.Values"/> indexes into this.</summary>
    [JsonPropertyName("textures")] public List<TexturePatternTextureEntry> Textures { get; set; } = [];
    [JsonPropertyName("materials")] public List<TexturePatternMaterialEntry> Materials { get; set; } = [];

    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static TexturePatternAnimManifest Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<TexturePatternAnimManifest>(stream, JsonOptions)
            ?? throw new InvalidDataException($"'{path}' did not deserialize to a texture pattern anim manifest.");
    }

    /// <summary>Every texture pattern anim's name next to a model's manifest (<c>&lt;modelName&gt;.&lt;AnimName&gt;.texpat.json</c>).</summary>
    public static IEnumerable<string> ListAvailable(string dataDirectory, string modelName) =>
        Directory.Exists(dataDirectory)
            ? Directory.EnumerateFiles(dataDirectory, $"{modelName}.*.texpat.json")
                .Select(Path.GetFileName)
                .Select(n => n![(modelName.Length + 1)..^".texpat.json".Length])
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            : [];

    /// <summary>
    /// The <see cref="SamplerBinding"/> for one of this anim's textures as seen through one sampler - the shape <see
    /// cref="TextureCache"/> loads from. <paramref name="samplerKey"/> matters because the sRGB decision reads it.
    /// </summary>
    public SamplerBinding? BindingFor(int textureIndex, string samplerKey)
    {
        if (textureIndex < 0 || textureIndex >= Textures.Count)
            return null;
        var t = Textures[textureIndex];
        if (string.IsNullOrEmpty(t.File))
            return null;
        return new SamplerBinding
        {
            Unit = -1, // the unit belongs to the shape's own sampler list, not to the anim
            Key = samplerKey,
            Assigned = samplerKey,
            Texture = t.Texture,
            File = t.File,
            Format = t.Format,
            Width = t.Width,
            Height = t.Height,
        };
    }
}
