using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Logging;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Sky.LensFlare;

/// <summary>The game's lens flare: <c>agl::pfx::Glare</c>'s <c>flare_filter_flare</c> program, decompiled out of <c>agl_technique_pfx.sharcb</c>.</summary>
internal sealed class LensFlarePass : IDisposable
{
    const int BlurIterations = 3;

    static readonly string BrightFrag = GlslFiles.Load("Totk/Sky/LensFlare/Bright.frag");

    // A 9-tap Gaussian folded into 5 bilinear fetches.
    static readonly string BlurFrag = GlslFiles.Load("Totk/Sky/LensFlare/Blur.frag");

    public readonly record struct Params(
        float Threshold, float GhostSpacing, Vector3 HaloTint, float HaloRadius, Vector3 Intensity, float Exposure, bool SkyOnly);

    readonly GL _gl;
    readonly uint _program, _brightProgram, _blurProgram;
    readonly FlareSource _source;
    readonly FlareQuad? _quad;
    bool _logged;

    public LensFlarePass(GL gl, ShaderProgramCache programs)
    {
        _gl = gl;
        _source = new FlareSource(gl);
        if (!programs.Exists("agl_flare_filter_flare"))
        {
            Log.Warning("[LensFlarePass] agl_flare_filter_flare not in the shader cache - disabled.");
            return;
        }

        _program = programs.Load("agl_flare_filter_flare");
        // RegisterUBO is at location 0 in both stages, so it decompiles to vp_c3 and fp_c3: one block under two names once linked, the
        // same collision CloudDomePass and SkyPostFxPass correct.
        _gl.BindUniformBlock(_program, "_vp_c3", LensFlareBlocks.Register.Binding);
        _gl.BindUniformBlock(_program, "_fp_c3", LensFlareBlocks.Register.Binding);
        _gl.UseProgram(_program);
        _gl.SetSamplerUnit(_program, "fp_t_tcb_8", 0); // cSrc

        _brightProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex330, BrightFrag, "lens_flare_bright");
        _blurProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex330, BlurFrag, "lens_flare_blur");
        _quad = new FlareQuad(gl);
        Log.Info("[LensFlarePass] real agl_flare_filter_flare linked (4 ghosts + halo).");
    }

    public bool Available => _program != 0;

    public void Run(GLResourceCache resources, RenderTargets targets, GpuTexture hdr, Params p)
    {
        if (!Available)
            return;

        BuildBlurredSource(resources, targets, hdr, p);
        LogFirstDraw(p);

        // The source is in display units but the flare is added before exposure, so exposure is divided back out of the intensity.
        resources.Bind(LensFlareBlocks.BuildRegister(p.GhostSpacing, p.HaloTint, p.HaloRadius, p.Intensity / MathF.Max(p.Exposure, 1e-4f)));
        AddFlare(targets, hdr);
    }

    public void Dispose()
    {
        foreach (uint program in new[] { _program, _brightProgram, _blurProgram })
            if (program != 0)
                _gl.ReleaseProgram(program);
        _source.Dispose();
        _quad?.Dispose();
    }

    // Quarter resolution and blurred: each ghost is the source magnified about 3x, so an unblurred source would give giant hard-edged
    // copies instead of soft blobs.
    void BuildBlurredSource(GLResourceCache resources, RenderTargets targets, GpuTexture hdr, Params p)
    {
        _source.Ensure(Math.Max(1, hdr.Width / 4), Math.Max(1, hdr.Height / 4));

        _source.BindTarget(0);
        _gl.Viewport(0, 0, (uint)_source.Width, (uint)_source.Height);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.Blend);
        _gl.UseProgram(_brightProgram);
        _gl.BindTextureUniform(_brightProgram, "tSrc", 0, hdr.Handle);
        _gl.SetFloat(_brightProgram, "uThreshold", p.Threshold);
        _gl.SetFloat(_brightProgram, "uExposure", MathF.Max(p.Exposure, 1e-4f));
        _gl.BindTextureUniform(_brightProgram, "tDepth", 1, targets.GBufferDepth.Handle);
        _gl.SetInt(_brightProgram, "uSkyOnly", p.SkyOnly ? 1 : 0);
        _gl.SetVec2(_brightProgram, "uSrcTexel", new Vector2(1f / hdr.Width, 1f / hdr.Height));
        resources.DrawFullscreenTriangle();

        _gl.UseProgram(_blurProgram);
        var texel = new Vector2(1f / _source.Width, 1f / _source.Height);
        for (int i = 0; i < BlurIterations; i++)
        {
            // Widen each iteration so a few cheap passes reach a large radius.
            float spread = 1f + i;
            BlurInto(resources, 1, _source.Texture(0), new Vector2(texel.X * spread, 0f));
            BlurInto(resources, 0, _source.Texture(1), new Vector2(0f, texel.Y * spread));
        }
    }

    void BlurInto(GLResourceCache resources, int target, uint source, Vector2 step)
    {
        _source.BindTarget(target);
        _gl.BindTextureUniform(_blurProgram, "tSrc", 0, source);
        _gl.SetVec2(_blurProgram, "uStep", step);
        resources.DrawFullscreenTriangle();
    }

    // Additive over the HDR buffer before exposure and tonemap: a flare is light the lens adds, so it belongs in linear space.
    void AddFlare(RenderTargets targets, GpuTexture hdr)
    {
        targets.BindColorTarget(hdr);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFuncSeparate(GLEnum.One, GLEnum.One, GLEnum.Zero, GLEnum.One);
        _gl.BlendEquationSeparate(GLEnum.FuncAdd, GLEnum.FuncAdd);
        _gl.UseProgram(_program);
        _gl.BindTextureAt(0, _source.Texture(0));
        _quad!.Draw();
        _gl.Disable(EnableCap.Blend);
    }

    void LogFirstDraw(Params p)
    {
        if (_logged)
            return;
        _logged = true;
        Log.Info($"[LensFlarePass] first draw: threshold={p.Threshold:G4} spacing={p.GhostSpacing:G4} haloRadius={p.HaloRadius:G4} intensity={p.Intensity}");
    }
}
