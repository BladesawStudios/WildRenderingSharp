using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Materials;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Profiles.Totk.Deferred.Resolve;
using WildRenderingSharp.Profiles.Totk.Shaders;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Deferred.PreShading;

/// <summary>Which pixels a pre-shading pass draws: those the pass-ID mask names, and the unstamped ones that have geometry when the pass also lights those.</summary>
internal sealed record PreShadingMask(uint PassIdTexture, uint DepthTexture, IReadOnlySet<int> PassIds, bool ClaimsUnstamped, Vector2 TargetSize);

/// <summary>
/// One of the game's pre-shading programs over a fullscreen quad, drawn only where its mask allows, which stands in for the depth test against the
/// material ID that the game's own quad relies on.
/// </summary>
/// <remarks>
/// The pass-ID mask is stored the way the frame is shown and the pass is drawn the way the G-buffer is, upside down from it, so the mask is read from the
/// opposite row; the depth is stored like the G-buffer and is read from the same one. Both are fetched by texel, the first of the four a pixel covers,
/// since a pixel's centre lies exactly between them and a filtered read there rounds either way from row to row.
/// </remarks>
internal sealed class PreShadingPass : IDisposable
{
    public const int PassIdUnit = 20, DepthUnit = 21, MaxPassIds = 64;

    const string PassIdSampler = "uPassIdTex", DepthSampler = "uDepthTex", AllowedUniform = "uAllowed", TargetSizeUniform = "uTargetSize",
        ClaimUniform = "uClaimUnstamped";

    // Linear depth at or past this is the sky.
    const float SkyDepth = 0.9999f;

    readonly GL _gl;
    readonly uint _program;
    readonly MaterialBlock _material;

    PreShadingPass(GL gl, uint program, MaterialBlock material)
    {
        _gl = gl;
        _program = program;
        _material = material;
    }

    // The pass named, or null when its program or its material was not exported.
    public static PreShadingPass? Load(GL gl, ShaderProgramCache programs, AssetDirectories directories, string name)
    {
        string? baseName = DeferredPassLoader.Find(directories.Decompiled, name);
        string materialPath = Path.Combine(directories.DeferredMaterials, $"{name}.gsys_material.bin");
        if (baseName is null || !File.Exists(materialPath))
            return null;
        return new PreShadingPass(gl, programs.Load(baseName, patchFragment: Patch), MaterialBlock.FromFile(gl, materialPath));
    }

    public unsafe void Draw(GLResourceCache resources, PreShadingMask mask)
    {
        int* allowed = stackalloc int[MaxPassIds];
        for (int id = 0; id < MaxPassIds; id++)
            allowed[id] = mask.PassIds.Contains(id) ? 1 : 0;

        resources.BindMaterial(_material);
        _gl.UseProgram(_program);
        _gl.SetVec2(_program, TargetSizeUniform, mask.TargetSize);
        _gl.SetInt(_program, ClaimUniform, mask.ClaimsUnstamped ? 1 : 0);
        _gl.BindTextureUniform(_program, PassIdSampler, PassIdUnit, mask.PassIdTexture);
        _gl.BindTextureUniform(_program, DepthSampler, DepthUnit, mask.DepthTexture);
        _gl.Uniform1(_gl.UniformLocation(_program, AllowedUniform), (uint)MaxPassIds, allowed);
        resources.DrawFullscreenQuadStrip();
    }

    public void Dispose()
    {
        _gl.ReleaseProgram(_program);
        _material.Dispose();
    }

    // The pass is drawn at the half-resolution buffers' size, so its own half-resolution depth texel is the one gathered at the pixel's centre. The water pass reads none.
    static string Patch(string fragment) =>
        MaskByPassId(PixelCentreGather.Anchor(ScaleShadowFilter(fragment), "cTex_HalfNormalizedLinearDepth"));

    // The character programs space their shadow taps by the scene material's filter width. The field programs space theirs by a width worked out in the shader, one texel and
    // more, which the same width scales, so one setting widens or narrows the shadows of both.
    static string ScaleShadowFilter(string fragment) =>
        fragment.Contains("} fp_c13;", StringComparison.Ordinal)
            ? FilterWidth.Replace(fragment, "$1 * fp_c13.data[33].x$2")
            : fragment;

    static readonly System.Text.RegularExpressions.Regex FilterWidth = new(@"(= fma\(temp_\d+, 1\.5, 1\.0\))(;)");

    static string MaskByPassId(string fragment) =>
        fragment.ReplaceRequired("void main()\n{",
            $"uniform sampler2D {PassIdSampler};\nuniform sampler2D {DepthSampler};\nuniform vec2 {TargetSizeUniform};\nuniform int {ClaimUniform};\n" +
            $"uniform int {AllowedUniform}[{MaxPassIds}];\n" +
            "bool ServesPixel(ivec2 pixel)\n{\n" +
            $"    int passId = int(texelFetch({PassIdSampler}, ivec2(pixel.x, 2 * int({TargetSizeUniform}.y) - 1 - pixel.y), 0).r * 255.0 + 0.5);\n" +
            $"    bool served = passId < {MaxPassIds} && {AllowedUniform}[passId] != 0;\n" +
            $"    if (!served && passId == 0 && {ClaimUniform} != 0)\n" +
            $"        served = texelFetch({DepthSampler}, pixel, 0).r < {SkyDepth.ToString(System.Globalization.CultureInfo.InvariantCulture)};\n" +
            "    return served;\n}\n" +
            "void preshadingMain()\n{")
        // One texel of the pre-shading buffers covers four pixels, and a texel with any of them in the pass is drawn whole, at the nearest surface's depth.
        // Leaving its fog out would leave the edge pixels of a surface unfogged against its fogged interior, as a pale rim.
        + "\nvoid main()\n{\n"
        + "    ivec2 corner = 2 * ivec2(gl_FragCoord.xy);\n"
        + "    if (!(ServesPixel(corner) || ServesPixel(corner + ivec2(1, 0)) || ServesPixel(corner + ivec2(0, 1)) || ServesPixel(corner + ivec2(1, 1))))\n"
        + "        discard;\n"
        + "    preshadingMain();\n}\n";
}
