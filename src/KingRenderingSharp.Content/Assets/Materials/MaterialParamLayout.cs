using System.Text.Json;
using System.Text.Json.Serialization;
using KingRenderingSharp.Core.Logging;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Content.Assets.Materials;

/// <summary>
/// Where each named shader parameter lives inside a material's <c>gsys_material</c> block, derived from the material's shading model.
/// </summary>
internal sealed class MaterialParamLayout
{
    [JsonPropertyName("material")] public string Material { get; set; } = "";
    [JsonPropertyName("block_size")] public int BlockSize { get; set; }
    [JsonPropertyName("uniforms")] public List<MaterialUniformEntry> Uniforms { get; set; } = [];

    Dictionary<string, int>? _byName;
    Dictionary<string, MaterialUniformEntry>? _entryByName;

    public bool TryGetOffset(string paramName, out int offset)
    {
        _byName ??= Uniforms
            .GroupBy(u => u.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Offset, StringComparer.Ordinal);
        return _byName.TryGetValue(paramName, out offset);
    }

    public bool TryGetEntry(string paramName, out MaterialUniformEntry entry)
    {
        _entryByName ??= Uniforms
            .GroupBy(u => u.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return _entryByName.TryGetValue(paramName, out entry!);
    }
}
