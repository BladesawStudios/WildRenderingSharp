using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>Every target of one material.</summary>
public sealed class MaterialAnimMaterialEntry
{
    [JsonPropertyName("material")] public string Material { get; set; } = "";
    [JsonPropertyName("targets")] public List<MaterialAnimTarget> Targets { get; set; } = [];
}
