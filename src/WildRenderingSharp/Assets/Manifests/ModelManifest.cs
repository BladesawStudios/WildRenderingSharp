using System.Text.Json;
using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets.Manifests;

/// <summary>Deserialized <c>&lt;Model&gt;.manifest.json</c>, written by <c>ShaderLibrary.CompileTool.ExportManifest</c>.</summary>
internal sealed class ModelManifest
{
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("vertex_stride")] public int VertexStride { get; set; } = 192;
    [JsonPropertyName("vertex_layout")] public List<VertexLayoutEntry> VertexLayout { get; set; } = [];
    [JsonPropertyName("shapes")] public List<ShapeManifestEntry> Shapes { get; set; } = [];

    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static ModelManifest Load(string manifestPath)
    {
        using var stream = File.OpenRead(manifestPath);
        var manifest = JsonSerializer.Deserialize<ModelManifest>(stream, JsonOptions)
            ?? throw new InvalidDataException($"'{manifestPath}' did not deserialize to a manifest.");
        manifest.ClampEyes();
        return manifest;
    }

    // An eye's textures are read through coordinates that are moved, scaled and turned by hand, which the game never does, so a texture the game tiles would tile in the eye again once the
    // coordinates leave 0..1. Every sampler of a material named for an eye clamps instead, which is the same everywhere the coordinates stay inside.
    void ClampEyes()
    {
        foreach (var shape in Shapes.Where(s => s.Material.Contains("eye", StringComparison.OrdinalIgnoreCase)))
            foreach (var sampler in shape.Samplers.Concat(shape.ZOnlySamplers).Concat(shape.MaterialSamplers))
                (sampler.WrapU, sampler.WrapV) = ("Clamp", "Clamp");
    }

    public static IEnumerable<string> ListAvailableModels(string cacheRoot) =>
        Directory.Exists(cacheRoot)
            ? Directory.EnumerateDirectories(cacheRoot)
                .Select(Path.GetFileName)
                .Where(n => n is not null && File.Exists(Path.Combine(cacheRoot, n, $"{n}.manifest.json")))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            : [];
}
