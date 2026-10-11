using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets.Manifests;

/// <summary>
/// One texture binding from a shape's <c>samplers</c>, <c>zonly_samplers</c> or <c>material_samplers</c> manifest list: a shader
/// sampler unit joined through the material's sampler assignment to the exported texture file.
/// </summary>
public sealed class SamplerBinding
{
    [JsonPropertyName("unit")] public int Unit { get; set; }
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("assigned")] public string Assigned { get; set; } = "";
    [JsonPropertyName("texture")] public string Texture { get; set; } = "";
    // File name relative to the data directory, or empty if the texture failed to export (e.g. an unsupported format); the
    // manifest still lists the binding so it can be logged.
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("format")] public string Format { get; set; } = "";
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    // The container's channel-selector bytes (0=R, 1=G, 2=B, 3=A, 4/5 constant 0/1) applied by the texture uploader, or null when the manifest
    // predates them, so the format heuristic decides; an int[] because System.Text.Json's byte[] converter expects base64.
    [JsonPropertyName("comp_select")] public int[]? CompSelect { get; set; }

    // The authored GX2 wrap mode of the sampler's U/V axes (GX2TexClamp's names), applied by the texture uploader; null on a manifest that predates
    // it, which keeps Wrap. Hardcoding GL_REPEAT made textures the game clamps tile once animated UVs left 0..1.
    [JsonPropertyName("wrap_u")] public string? WrapU { get; set; }
    [JsonPropertyName("wrap_v")] public string? WrapV { get; set; }
}
