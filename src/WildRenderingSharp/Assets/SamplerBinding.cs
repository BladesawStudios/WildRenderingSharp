using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>One texture binding from a shape's <c>samplers</c>, <c>zonly_samplers</c> or <c>material_samplers</c> manifest list: a shader sampler unit joined through the material's sampler assignment to the exported texture file. See <c>ExportManifest.BuildSamplers</c>.</summary>
public sealed class SamplerBinding
{
    [JsonPropertyName("unit")] public int Unit { get; set; }
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("assigned")] public string Assigned { get; set; } = "";
    [JsonPropertyName("texture")] public string Texture { get; set; } = "";
    /// <summary>File name relative to the data directory, or empty if the texture failed to export (e.g. an unsupported format); the manifest still lists the binding so it can be logged.</summary>
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("format")] public string Format { get; set; } = "";
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    /// <summary>
    /// The container's per-texture channel-selector bytes (source channel per output, R/G/B/A order: <c>0=R, 1=G, 2=B, 3=A</c>, with <c>4/5</c> meaning constant 0/1 on other Nintendo containers, not observed
    /// in this game's TXTG data). Applied in <c>TextureCache.ApplySwizzle</c>, whose remarks explain why an earlier Ghidra-derived encoding was reverted. Null when the manifest predates the field or the TXTG
    /// could not be read, so the caller falls back to the format-based heuristic instead of applying a wrong swizzle. <c>int[]</c>, not <c>byte[]</c>, because <c>System.Text.Json</c>'s <c>byte[]</c> converter
    /// expects a base64 string.
    /// </summary>
    [JsonPropertyName("comp_select")] public int[]? CompSelect { get; set; }

    /// <summary>
    /// The authored GX2 wrap mode for this sampler's U/V axes (<c>GX2TexClamp</c>'s names: "Wrap", "Mirror", "Clamp", "ClampBorder", "ClampToEdge", ...; see <c>ExportManifest.BuildSamplers</c>), applied in
    /// <c>TextureCache.MapWrapMode</c>. Hardcoding GL_REPEAT made textures the game clamps (an eye iris scrolled by a texture-SRT anim) tile once animated UVs left 0..1. Null on a manifest prepared before
    /// the field existed, which falls back to Wrap.
    /// </summary>
    [JsonPropertyName("wrap_u")] public string? WrapU { get; set; }
    [JsonPropertyName("wrap_v")] public string? WrapV { get; set; }
}
