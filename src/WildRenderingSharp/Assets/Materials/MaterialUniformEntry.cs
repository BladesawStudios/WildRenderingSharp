using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets.Materials;

/// <summary>One parameter's byte layout inside a compiled <c>gsys_material</c> block.</summary>
internal sealed class MaterialUniformEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("offset")] public int Offset { get; set; }

    // The BFRES ShaderParamType name the material declares for this parameter, or null when the uniform sits at the shader's default with no
    // override, which a live editor should treat as no known widget.
    [JsonPropertyName("type")] public string? Type { get; set; }

    // Whether the shape's own decompiled shader references this parameter; null when unknown (an older cache, or Type is null), which a live editor
    // should treat as true.
    [JsonPropertyName("used")] public bool? Used { get; set; }

    // The authored (mode, scaleX, scaleY, rotation, translateX, translateY) of a TexSrt parameter, null for other types.
    // An animation that drives one sub-field re-bakes the set from this baseline.
    [JsonPropertyName("raw_srt")] public float[]? RawSrt { get; set; }
}
