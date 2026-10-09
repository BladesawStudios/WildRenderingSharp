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

    // The material's AUTHORED (mode, scaleX, scaleY, rotation, translateX, translateY) for a TexSrt/TexSrtEx-typed parameter
    // only - null for every other type. This is the pre-bake form; MaterialAnimPose needs it because an animation can drive
    // just one sub-field (a scroll anim touching only translateY, say) and has to re-bake the whole six-value set every frame,
    // using this as the baseline for whichever sub-fields the anim leaves untouched - see that class's own remarks and
    // TexSrtBake.
    [JsonPropertyName("raw_srt")] public float[]? RawSrt { get; set; }
}
