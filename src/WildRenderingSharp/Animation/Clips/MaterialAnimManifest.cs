using System.Text.Json;
using System.Text.Json.Serialization;

namespace WildRenderingSharp.Animation.Clips;

/// <summary>
/// Metadata for a material animation. These may animate texture SRT
/// (scale/rotate/translate), color data, or shader parameters
/// (i.e. BFRES <c>_fts</c>, <c>_fcl</c>, or <c>_fsp</c>).
/// This is deserialized from <c>&lt;Model&gt;.&lt;AnimName&gt;.matanim.json</c> in the cache.
/// </summary>
public sealed class MaterialAnimManifest : IAnimClip
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    
    // TODO: Make this an enum
    /// <summary>
    /// "TexSrt", "ShaderParam", or "Color".
    /// BfresLibrary marks TexSrt animations as ShaderParam, but we separate them.
    /// </summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("frame_count")] public int FrameCount { get; set; }
    [JsonPropertyName("loop")] public bool Loop { get; set; }
    
    // There may be many curves outputting to many materials.
    [JsonPropertyName("materials")] public List<MaterialAnimMaterialEntry> Materials { get; set; } = [];

    public bool IsTextureSrt => string.Equals(Kind, "TexSrt", StringComparison.Ordinal);

    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static MaterialAnimManifest Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<MaterialAnimManifest>(stream, JsonOptions)
            ?? throw new InvalidDataException($"'{path}' did not deserialize to a material anim manifest.");
    }

    public static IEnumerable<string> ListAvailable(string dataDirectory, string modelName) =>
        Directory.Exists(dataDirectory)
            ? Directory.EnumerateFiles(dataDirectory, $"{modelName}.*.matanim.json")
                .Select(Path.GetFileName)
                .Select(n => n![(modelName.Length + 1)..^".matanim.json".Length])
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            : [];
}
