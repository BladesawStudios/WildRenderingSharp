using System.Text.Json;
using System.Text.Json.Serialization;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Content.Assets.Manifests;

/// <summary>What a model's shapes are: their materials, programs, samplers and render state, as the pack's records say.</summary>
internal sealed class ModelManifest
{
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("vertex_stride")] public int VertexStride { get; set; } = 192;
    [JsonPropertyName("vertex_layout")] public List<VertexLayoutEntry> VertexLayout { get; set; } = [];
    [JsonPropertyName("shapes")] public List<ShapeManifestEntry> Shapes { get; set; } = [];

    // An eye's textures are read through coordinates that are moved, scaled and turned by hand, which the game never does, so a texture the game tiles would tile in the eye again once the
    // coordinates leave 0..1. Every sampler of a material named for an eye clamps instead, which is the same everywhere the coordinates stay inside.
    internal void ClampEyes()
    {
        foreach (var shape in Shapes.Where(s => s.Material.Contains("eye", StringComparison.OrdinalIgnoreCase)))
            foreach (var sampler in shape.Samplers.Concat(shape.ZOnlySamplers).Concat(shape.MaterialSamplers))
                (sampler.WrapU, sampler.WrapV) = ("Clamp", "Clamp");
    }

    /// <summary>The names of the models a cache has packs for.</summary>
    public static IEnumerable<string> ListAvailableModels(string cacheRoot)
    {
        string packs = Path.Combine(cacheRoot, "_packs");
        return Directory.Exists(packs)
            ? Directory.EnumerateFiles(packs, "*.bfap").Select(f => Path.GetFileNameWithoutExtension(f)).Order(StringComparer.OrdinalIgnoreCase)
            : [];
    }
}
