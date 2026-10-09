using System.Text.Json;
using System.Text.Json.Serialization;
using WildRenderingSharp.Assets.Manifests;

namespace WildRenderingSharp.Animation.Clips;

/// <summary>Deserialized <c>&lt;Model&gt;.&lt;AnimName&gt;.texpat.json</c>, the texture pattern anim exported from a BFRES.</summary>
public sealed class TexturePatternAnimManifest : IAnimClip
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("frame_count")] public int FrameCount { get; set; }
    [JsonPropertyName("loop")] public bool Loop { get; set; }
    // The anim's own texture list - Values indexes into this.
    [JsonPropertyName("textures")] public List<TexturePatternTextureEntry> Textures { get; set; } = [];
    [JsonPropertyName("materials")] public List<TexturePatternMaterialEntry> Materials { get; set; } = [];

    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static TexturePatternAnimManifest Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<TexturePatternAnimManifest>(stream, JsonOptions)
            ?? throw new InvalidDataException($"'{path}' did not deserialize to a texture pattern anim manifest.");
    }

    public static IEnumerable<string> ListAvailable(string dataDirectory, string modelName) =>
        Directory.Exists(dataDirectory)
            ? Directory.EnumerateFiles(dataDirectory, $"{modelName}.*.texpat.json")
                .Select(Path.GetFileName)
                .Select(n => n![(modelName.Length + 1)..^".texpat.json".Length])
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            : [];

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
