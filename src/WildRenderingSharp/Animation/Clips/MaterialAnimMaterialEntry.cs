using System.Text.Json.Serialization;

namespace WildRenderingSharp.Animation.Clips;

/// <summary>A set of animation curves targeting a single material.</summary>
public sealed class MaterialAnimMaterialEntry
{
    [JsonPropertyName("material")] public string Material { get; set; } = "";
    [JsonPropertyName("targets")] public List<MaterialAnimTarget> Targets { get; set; } = [];
}
