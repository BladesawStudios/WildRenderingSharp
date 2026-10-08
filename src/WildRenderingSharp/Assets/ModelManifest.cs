using System.Text.Json;
using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>
/// Deserialized <c>&lt;Model&gt;.manifest.json</c>, written by
/// <c>ShaderLibrary.CompileTool.ExportManifest</c>. This is the one file a model needs to be
/// fully describable to WildRenderingSharp with no BFRES parsing of its own.
/// </summary>
public sealed class ModelManifest
{
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("vertex_stride")] public int VertexStride { get; set; } = 192;
    [JsonPropertyName("vertex_layout")] public List<VertexLayoutEntry> VertexLayout { get; set; } = [];
    [JsonPropertyName("shapes")] public List<ShapeManifestEntry> Shapes { get; set; } = [];

    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static ModelManifest Load(string manifestPath)
    {
        using var stream = File.OpenRead(manifestPath);
        return JsonSerializer.Deserialize<ModelManifest>(stream, JsonOptions)
            ?? throw new InvalidDataException($"'{manifestPath}' did not deserialize to a manifest.");
    }

    /// <summary>
    /// Finds every prepared model under a cache root - one subdirectory per model
    /// (<c>&lt;cacheRoot&gt;/&lt;ModelName&gt;/&lt;ModelName&gt;.manifest.json</c>, written by
    /// <c>ModelPreparer</c>), rather than every model's files sharing one flat
    /// directory - which is what let two models' same-named shapes/textures clobber each other.
    /// </summary>
    public static IEnumerable<string> ListAvailableModels(string cacheRoot) =>
        Directory.Exists(cacheRoot)
            ? Directory.EnumerateDirectories(cacheRoot)
                .Select(Path.GetFileName)
                .Where(n => n is not null && File.Exists(Path.Combine(cacheRoot, n, $"{n}.manifest.json")))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            : [];
}
