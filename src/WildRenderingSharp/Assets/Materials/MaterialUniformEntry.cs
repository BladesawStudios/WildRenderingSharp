using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets.Materials;

/// <summary>One parameter's byte layout inside a compiled <c>gsys_material</c> block.</summary>
public sealed class MaterialUniformEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("offset")] public int Offset { get; set; }

    /// <summary>
    /// The BFRES <c>ShaderParamType</c> name (e.g. "Float4", "Int", "TexSrt") the MATERIAL itself
    /// declares for this parameter, or null when the uniform sits at the shader's own default with
    /// no material override - there is then no authored type to report (the compiled block carries
    /// no component-count/type of its own on this platform). A live editor should treat null as
    /// "no known widget for this one" rather than guessing.
    /// </summary>
    [JsonPropertyName("type")] public string? Type { get; set; }

    /// <summary>
    /// Whether this shape's own real decompiled shader (G-buffer/Z-only/forward, whichever exist)
    /// actually references this parameter - null when unknown (an older cache predating this
    /// field, or <see cref="Type"/> is itself null). A live editor should treat null the same as
    /// true (show it) rather than hide anything it isn't certain about.
    /// </summary>
    [JsonPropertyName("used")] public bool? Used { get; set; }

    // The material's AUTHORED (mode, scaleX, scaleY, rotation, translateX, translateY) for a TexSrt/TexSrtEx-typed parameter
    // only - null for every other type. This is the pre-bake form; MaterialAnimPose needs it because an animation can drive
    // just one sub-field (a scroll anim touching only translateY, say) and has to re-bake the whole six-value set every frame,
    // using this as the baseline for whichever sub-fields the anim leaves untouched - see that class's own remarks and
    // TexSrtBake.
    [JsonPropertyName("raw_srt")] public float[]? RawSrt { get; set; }
}
