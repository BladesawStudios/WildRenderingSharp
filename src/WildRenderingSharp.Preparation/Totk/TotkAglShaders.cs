using ShaderLibrary;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>The engine's own screen shaders: HDR compose, the cloud, and the lens flare and sky post-fx chains.</summary>
public static class TotkAglShaders
{
    const string ApplicationPackage = "Shader/ApplicationPackage.Nin_NX_NVN.release.sarc.zs";
    const string AglResource = "Lib/agl/agl_resource.Nin_NX_NVN.release.sarc.zs";

    // Both sky variants must match the bake, which stores the sun view non-linearly.
    const string NonLinear = "1";

    static readonly string[] SkyPrecompute =
    [
        "sky_transmittance", "sky_irradiance", "sky_inscatter", "sky_delta_inscatter",
        "sky_copy_inscatter", "sky_copy_irradiance", "sky_bake_irradiance", "sky_bake_range_transmittance",
    ];

    // Only the variant without the colour-correction table is run, so it also stands in for the bare name.
    public static void HdrCompose(IRomAccess rom, string outDir)
    {
        var sharc = AglDecompiler.Open(rom, ApplicationPackage, "AglShader.sharcb");
        AglDecompiler.AllVariants(sharc, AglDecompiler.Program(sharc, "hdr_compose"), outDir);
        foreach (string extension in new[] { "vert", "frag" })
        {
            string variant = Path.Combine(outDir, $"agl_hdr_compose_ENABLE_COLOR_CORRECTION_TABLE0.{extension}");
            if (File.Exists(variant))
                File.Copy(variant, Path.Combine(outDir, $"agl_hdr_compose.{extension}"), overwrite: true);
        }
    }

    public static void Cloud(IRomAccess rom, string outDir)
    {
        var sharc = AglDecompiler.Open(rom, AglResource, "agl_technique.sharcb");
        AglDecompiler.Variant(sharc, AglDecompiler.Program(sharc, "cloud"), new()
        {
            ["TYPE_USE_TEX_BLEND"] = "1",
            ["TYPE_USE_DEBUG_SUN_DISP"] = "0",
            ["TYPE_USE_PROC_TEXTURE"] = "0",
            ["TYPE_USE_SCATTER"] = "1",
            ["TYPE_USE_NLD_SOFTPTCL"] = "0",
        }, outDir, "agl_cloud");
    }

    public static void LensFlare(IRomAccess rom, string outDir)
    {
        var sharc = AglDecompiler.Open(rom, AglResource, "agl_technique_pfx.sharcb");
        AglDecompiler.Variant(sharc, AglDecompiler.Program(sharc, "flare_filter_flare"),
            new() { ["GHOST_NUM"] = "4", ["IS_HALO"] = "1", ["IS_DISTORTION"] = "0" }, outDir, "agl_flare_filter_flare");

        foreach (string direction in new[] { "0", "1" })
            AglDecompiler.Variant(sharc, AglDecompiler.Program(sharc, "glare_filter_blur"),
                new() { ["BLUR_DIR"] = direction, ["BLUR_LV"] = "3", ["COLOR_BIAS"] = "1", ["IS_FLIP"] = "0" }, outDir, $"agl_glare_filter_blur{direction}");
    }

    public static void SkyPostFx(IRomAccess rom, string outDir)
    {
        var sharc = AglDecompiler.Open(rom, AglResource, "agl_technique_pfx.sharcb");
        var sky = AglDecompiler.Program(sharc, "sky_postfx_sky");
        foreach (var (fog, sun, name) in new[] { ("0", "0", "sky"), ("0", "1", "sky_sun"), ("1", "0", "sky_fog"), ("1", "1", "sky_sun_fog") })
            AglDecompiler.Variant(sharc, sky, new()
            {
                ["BAKED_SUNVIEW_NON_LINEAR"] = NonLinear, ["USE_ADHOC_FOG"] = fog, ["RENDER_SUN"] = sun,
                ["RENDER_CLOUD"] = "0", ["USE_TONEMAP"] = "0",
            }, outDir, $"agl_sky_postfx_{name}");

        AglDecompiler.Variant(sharc, AglDecompiler.Program(sharc, "sky_postfx_ground"), new()
        {
            ["BAKED_SUNVIEW_NON_LINEAR"] = NonLinear, ["USE_ADHOC_FOG"] = "0", ["USE_LINEAR_DEPTH"] = "1",
            ["USE_FOG_DENSITY"] = "0", ["USE_TONEMAP"] = "0",
        }, outDir, "agl_sky_postfx_ground");

        foreach (string name in SkyPrecompute)
            PrecomputeSteps(sharc, AglDecompiler.Program(sharc, name), name, outDir);

        AglDecompiler.Variant(sharc, AglDecompiler.Program(sharc, "sky_bake_inscatter"), new()
        {
            ["LOCAL_STEP"] = "0", ["BAKED_SUNVIEW_NON_LINEAR"] = NonLinear, ["ADHOC_PROC"] = "0",
        }, outDir, "agl_sky_bake_inscatter");
    }

    // Each LOCAL_STEP is one iteration of the offline solve, so every declared value is a pass the chain needs.
    static void PrecomputeSteps(SharcfbFile sharc, SharcfbFile.ShaderProgram program, string name, string outDir)
    {
        var steps = program.VariationMacros.FirstOrDefault(m => m.Name == "LOCAL_STEP");
        if (steps is null)
        {
            AglDecompiler.Variant(sharc, program, new(), outDir, $"agl_{name}");
            return;
        }
        foreach (string step in steps.Values)
            AglDecompiler.Variant(sharc, program, new() { ["LOCAL_STEP"] = step }, outDir, $"agl_{name}_step{step}");
    }
}
