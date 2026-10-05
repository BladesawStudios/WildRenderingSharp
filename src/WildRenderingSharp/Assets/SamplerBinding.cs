using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>
/// One texture binding from a shape's <c>samplers</c>/<c>zonly_samplers</c>/<c>material_samplers</c>
/// manifest list - a shader sampler unit joined through the material's own sampler assignment to
/// the actual exported texture file. See <c>ExportManifest.BuildSamplers</c>.
/// </summary>
public sealed class SamplerBinding
{
    [JsonPropertyName("unit")] public int Unit { get; set; }
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("assigned")] public string Assigned { get; set; } = "";
    [JsonPropertyName("texture")] public string Texture { get; set; } = "";
    /// <summary>File name relative to the data directory, or empty if the texture failed to export (e.g. an unsupported format) - the manifest still lists the binding so it can be logged, not silently dropped.</summary>
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("format")] public string Format { get; set; } = "";
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    /// <summary>
    /// The container's own real, authored per-texture channel-selector bytes (source channel per
    /// output, R/G/B/A order - <c>0=R,1=G,2=B,3=A</c>, with <c>4/5</c> meaning constant-0/constant-1
    /// on some other Nintendo texture containers, not observed in this game's TXTG data so far) -
    /// see <c>WildRenderingSharp.Assets.TextureCache.ApplySwizzle</c> for where this gets applied, and its
    /// own remarks for why an EARLIER attempt at this (a different, Ghidra-derived encoding, applied
    /// unconditionally) was reverted. Null when the manifest predates this field or the TXTG
    /// couldn't be read - a real absence, not a fake identity default, so the caller can fall back
    /// to the older format-based heuristic instead of applying a wrong swizzle.
    /// <c>int[]</c>, not <c>byte[]</c> - <c>System.Text.Json</c>'s built-in <c>byte[]</c> converter
    /// expects a base64 STRING, not a JSON array of numbers, and throws on a plain array like the
    /// manifest actually writes here.
    /// </summary>
    [JsonPropertyName("comp_select")] public int[]? CompSelect { get; set; }

    /// <summary>
    /// The real, authored GX2 wrap mode for this sampler's U/V axes (<c>GX2TexClamp</c>'s own
    /// names - "Wrap", "Mirror", "Clamp", "ClampBorder", "ClampToEdge", ... - see
    /// <c>ExportManifest.BuildSamplers</c>). Every material texture used to be uploaded with
    /// GL_REPEAT hardcoded regardless of this, which is why textures the game clamps (an eye
    /// iris scrolled by a texture-SRT anim, in particular) visibly tiled once animated UVs moved
    /// outside 0..1 - see <c>TextureCache.MapWrapMode</c> for where this is actually applied.
    /// Null on a manifest prepared before this field existed - falls back to Wrap (the previous,
    /// unconditional behaviour) rather than guessing.
    /// </summary>
    [JsonPropertyName("wrap_u")] public string? WrapU { get; set; }
    [JsonPropertyName("wrap_v")] public string? WrapV { get; set; }
}
