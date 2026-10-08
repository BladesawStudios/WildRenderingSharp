using System.Text.Json;
using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>
/// Deserialized <c>&lt;Model&gt;.&lt;AnimName&gt;.matanim.json</c> - a shader PARAMETER animation, which is the single mechanism
/// behind BFRES's <c>_fsp</c> (shader param), <c>_fcl</c> (colour) and <c>_fts</c> (texture SRT) anims alike.
/// </summary>
public sealed class MaterialAnimManifest : IAnimClip
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>"TexSrt" when every parameter this anim drives is an SRT type, else the BFRES bucket ("ShaderParam"/"Color"). Classified by what it actually touches, because BfresLibrary's own bucketing files <c>_fts</c> anims under ShaderParam.</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("frame_count")] public int FrameCount { get; set; }
    [JsonPropertyName("loop")] public bool Loop { get; set; }
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
