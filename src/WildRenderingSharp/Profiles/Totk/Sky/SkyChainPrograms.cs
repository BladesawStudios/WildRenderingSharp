using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>The real agl sky programs of the precompute chain, linked with their uniform blocks and sampler units bound.</summary>
sealed class SkyChainPrograms
{
    // Sampler units follow the fp_t_tcb_<hex> naming, where hex is 8 + 2 * the archive's sampler location; that is not the
    // archive's list order, so binding by position swaps textures.
    static readonly string[] Names =
    [
        "agl_sky_transmittance_step0",
        "agl_sky_irradiance_step1", "agl_sky_irradiance_step2", "agl_sky_irradiance_step3",
        "agl_sky_inscatter_step1", "agl_sky_inscatter_step2",
        "agl_sky_delta_inscatter_step2", "agl_sky_delta_inscatter_step3",
        "agl_sky_copy_inscatter_step1", "agl_sky_copy_inscatter_step2",
        "agl_sky_copy_irradiance_step0",
        "agl_sky_bake_inscatter",
    ];

    // The copy and bake passes take RenderInfo at c4; every solve pass takes Config there.
    static readonly HashSet<string> UsesRenderInfo =
    [
        "agl_sky_copy_inscatter_step1", "agl_sky_copy_inscatter_step2", "agl_sky_copy_irradiance_step0", "agl_sky_bake_inscatter",
    ];

    readonly Dictionary<string, uint> _programs = [];

    SkyChainPrograms()
    {
    }

    public uint this[string name] => _programs[name];

    // Links every program of the chain, or returns null when the shader cache lacks one.
    public static SkyChainPrograms? Load(GL gl, ShaderProgramCache cache)
    {
        foreach (string name in Names.Where(n => !cache.Exists(n)))
        {
            Console.WriteLine($"[SkyPrecomputePass] '{name}' missing from the shader cache - sky precompute disabled.");
            return null;
        }

        var chain = new SkyChainPrograms();
        foreach (string name in Names)
            chain._programs[name] = chain.Link(gl, cache, name);
        return chain;
    }

    // The decompiled per-stage block indices collide once both stages share one program, so each block is rebound explicitly.
    uint Link(GL gl, ShaderProgramCache cache, string name)
    {
        uint program = cache.Load(name);
        uint c4 = UsesRenderInfo.Contains(name) ? SkyPrecomputeBlocks.RenderInfo.Binding : SkyPrecomputeBlocks.Config.Binding;
        gl.BindUniformBlock(program, "_fp_c3", SkyPrecomputeBlocks.SizeInfo.Binding);
        gl.BindUniformBlock(program, "_vp_c3", SkyPrecomputeBlocks.SizeInfo.Binding);
        gl.BindUniformBlock(program, "_fp_c4", c4);
        gl.BindUniformBlock(program, "_vp_c4", c4);

        gl.UseProgram(program);
        foreach (var (uniform, unit) in SamplerUnits(name))
            gl.SetSamplerUnit(program, uniform, unit);
        return program;
    }

    static (string Uniform, int Unit)[] SamplerUnits(string name) => name switch
    {
        // cTexTransmittance, cTexDeltaSR, cTexDeltaE or cTexInscatter alone.
        "agl_sky_irradiance_step1" or "agl_sky_inscatter_step1" or "agl_sky_irradiance_step3" or "agl_sky_copy_irradiance_step0"
            or "agl_sky_bake_inscatter" or "agl_sky_copy_inscatter_step2" => [("fp_t_tcb_8", 0)],

        // SR@0 and SM@1, or T@0 and deltaJ@1.
        "agl_sky_irradiance_step2" or "agl_sky_copy_inscatter_step1" or "agl_sky_inscatter_step2" => [("fp_t_tcb_8", 0), ("fp_t_tcb_A", 1)],

        // T@0, E@1, SR@2, SM@3.
        "agl_sky_delta_inscatter_step2" => [("fp_t_tcb_8", 0), ("fp_t_tcb_A", 1), ("fp_t_tcb_C", 2), ("fp_t_tcb_E", 3)],

        // T@0, E@1, SR@2.
        "agl_sky_delta_inscatter_step3" => [("fp_t_tcb_8", 0), ("fp_t_tcb_A", 1), ("fp_t_tcb_C", 2)],

        _ => [],
    };
}
