using System.Text.Json.Serialization;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Assets.Manifests;

/// <summary>A material's render state as the engine's RenderInfo strings; mode "custom" means the blend and depth fields are authoritative rather than a named preset.</summary>
internal sealed class RenderState
{
    [JsonPropertyName("mode")] public string Mode { get; set; } = "";
    [JsonPropertyName("blend")] public bool Blend { get; set; }
    [JsonPropertyName("blend_mode")] public string BlendMode { get; set; } = "";
    [JsonPropertyName("rgb_op")] public string RgbOp { get; set; } = "";
    [JsonPropertyName("rgb_src")] public string RgbSrc { get; set; } = "";
    [JsonPropertyName("rgb_dst")] public string RgbDst { get; set; } = "";
    [JsonPropertyName("alpha_op")] public string AlphaOp { get; set; } = "";
    [JsonPropertyName("alpha_src")] public string AlphaSrc { get; set; } = "";
    [JsonPropertyName("alpha_dst")] public string AlphaDst { get; set; } = "";
    [JsonPropertyName("depth_write")] public string DepthWrite { get; set; } = "true";
    [JsonPropertyName("depth_func")] public string DepthFunc { get; set; } = "lequal";
    [JsonPropertyName("display_face")] public string DisplayFace { get; set; } = "";

    public bool DepthWriteEnabled => DepthWrite == "true";

    public readonly record struct BlendFuncs(GLEnum SrcRgb, GLEnum DstRgb, GLEnum SrcAlpha, GLEnum DstAlpha);
    public readonly record struct BlendEquations(GLEnum Rgb, GLEnum Alpha);

    static readonly Dictionary<string, GLEnum> BlendFactors = new()
    {
        ["zero"] = GLEnum.Zero, ["one"] = GLEnum.One,
        ["src_color"] = GLEnum.SrcColor, ["one_minus_src_color"] = GLEnum.OneMinusSrcColor,
        ["src_alpha"] = GLEnum.SrcAlpha, ["one_minus_src_alpha"] = GLEnum.OneMinusSrcAlpha,
        ["dst_alpha"] = GLEnum.DstAlpha, ["one_minus_dst_alpha"] = GLEnum.OneMinusDstAlpha,
        ["dst_color"] = GLEnum.DstColor, ["one_minus_dst_color"] = GLEnum.OneMinusDstColor,
        ["src_alpha_saturate"] = GLEnum.SrcAlphaSaturate,
        ["constant_color"] = GLEnum.ConstantColor, ["one_minus_constant_color"] = GLEnum.OneMinusConstantColor,
        ["constant_alpha"] = GLEnum.ConstantAlpha, ["one_minus_constant_alpha"] = GLEnum.OneMinusConstantAlpha,
    };

    static readonly Dictionary<string, GLEnum> BlendOps = new()
    {
        ["add"] = GLEnum.FuncAdd, ["sub"] = GLEnum.FuncSubtract, ["subtract"] = GLEnum.FuncSubtract,
        ["reverse_sub"] = GLEnum.FuncReverseSubtract, ["min"] = GLEnum.Min, ["max"] = GLEnum.Max,
    };

    static readonly Dictionary<string, GLEnum> DepthFuncsByName = new()
    {
        ["never"] = GLEnum.Never, ["less"] = GLEnum.Less, ["equal"] = GLEnum.Equal, ["lequal"] = GLEnum.Lequal,
        ["greater"] = GLEnum.Greater, ["notequal"] = GLEnum.Notequal, ["gequal"] = GLEnum.Gequal, ["always"] = GLEnum.Always,
    };

    static GLEnum Lookup(Dictionary<string, GLEnum> table, string? value, string fallbackKey) =>
        table.GetValueOrDefault(string.IsNullOrEmpty(value) ? fallbackKey : value, table[fallbackKey]);

    public (BlendFuncs Funcs, BlendEquations Ops) ResolveBlendState() => (
        new BlendFuncs(
            Lookup(BlendFactors, RgbSrc, "src_alpha"), Lookup(BlendFactors, RgbDst, "one_minus_src_alpha"),
            Lookup(BlendFactors, AlphaSrc, "one"), Lookup(BlendFactors, AlphaDst, "zero")),
        new BlendEquations(Lookup(BlendOps, RgbOp, "add"), Lookup(BlendOps, AlphaOp, "add")));

    public GLEnum ResolveDepthFunc() => Lookup(DepthFuncsByName, DepthFunc, "lequal");
}
