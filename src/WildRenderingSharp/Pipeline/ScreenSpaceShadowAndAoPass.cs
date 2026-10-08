using WildRenderingSharp.Graphics;
using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Produces the two screen-space buffers the deferred resolve shaders expect from the unimplemented <c>preshading_*</c> passes:
/// <c>cTex_PreShadow</c> (sun visibility, Poisson-disc PCF against the shadow map) and <c>cTex_PreMisc</c> (an alchemy-style AO
/// plus the diffuse N.L the <c>chara_*</c> resolve passes read from <c>.y</c>).
/// </summary>
public sealed class ScreenSpaceShadowAndAoPass : IDisposable
{
    readonly GL _gl;
    readonly uint _preshadowProgram, _preshadingFilterProgram, _preshadingReduceProgram, _preshadingUpsampleProgram, _aoProgram, _blurProgram;

    public const float AoRadiusWorld = 0.035f;
    public const float AoStrength = 0.85f;
    public const float ShadowBiasWorld = 0.0015f;

    static readonly string PreshadowFragmentSource = GlslFiles.Load("Pipeline/ScreenSpaceShadowAndAo/Preshadow.frag");


    static readonly string AoFragmentSource = GlslFiles.Load("Pipeline/ScreenSpaceShadowAndAo/Ao.frag");

    static readonly string BlurFragmentSource = GlslFiles.Load("Pipeline/ScreenSpaceShadowAndAo/Blur.frag");

    static readonly string PreshadingFilterFragmentSource = GlslFiles.Load("Pipeline/ScreenSpaceShadowAndAo/PreshadingFilter.frag");

    static readonly string PreshadingReduceFilterFragmentSource = GlslFiles.Load("Pipeline/ScreenSpaceShadowAndAo/PreshadingReduceFilter.frag");

    static readonly string PreshadingUpsampleFragmentSource = GlslFiles.Load("Pipeline/ScreenSpaceShadowAndAo/PreshadingUpsample.frag");


    public ScreenSpaceShadowAndAoPass(GL gl)
    {
        _gl = gl;
        _preshadowProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, PreshadowFragmentSource, "preshadow");
        _preshadingFilterProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, PreshadingFilterFragmentSource, "preshading_filter");
        _preshadingReduceProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, PreshadingReduceFilterFragmentSource, "preshading_reduce");
        _preshadingUpsampleProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, PreshadingUpsampleFragmentSource, "preshading_upsample");
        _aoProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, AoFragmentSource, "ao");
        _blurProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, BlurFragmentSource, "ao_blur");
    }

    public readonly record struct Params(
        Vector4[] ViewInv3Rows, Vector4[] LightViewProj, Vector2 TanHalf, Vector3 SunWorld, Vector3 SunView,
        float Near, float Far, float ShadowBias, float ShadowTexel, float ShadowTexelWorld, float ShadowDepthRange,
        float AoRadius, float AoStrength, uint ShadowTexture,
        CascadeParams? Cascades = null);

    /// <summary>The cascades a frame's shadows come from - see <see cref="FrameRequest.ShadowCascades"/>.</summary>
    public sealed record CascadeParams(uint Texture, Vector4[][] ViewProj, float[] TexelWorld, float[] Bias);

    public void Run(GLResourceCache resources, RenderTargets targets, Params p)
    {
        _gl.Disable(EnableCap.DepthTest);

        // PreShadow: raw PCF first.
        _gl.UseProgram(_preshadowProgram);
        SetMat4(_preshadowProgram, "uViewInv", Rendering.Mat4Math.ToMat4(p.ViewInv3Rows));
        SetMat4(_preshadowProgram, "uLightViewProj", p.LightViewProj);
        _gl.Uniform2(_gl.GetUniformLocation(_preshadowProgram, "uTanHalf"), p.TanHalf.X, p.TanHalf.Y);
        _gl.Uniform3(_gl.GetUniformLocation(_preshadowProgram, "uSunWorld"), p.SunWorld.X, p.SunWorld.Y, p.SunWorld.Z);
        _gl.Uniform1(_gl.GetUniformLocation(_preshadowProgram, "uNear"), p.Near);
        _gl.Uniform1(_gl.GetUniformLocation(_preshadowProgram, "uFar"), p.Far);
        _gl.Uniform1(_gl.GetUniformLocation(_preshadowProgram, "uBias"), p.ShadowBias);
        _gl.Uniform1(_gl.GetUniformLocation(_preshadowProgram, "uTexel"), p.ShadowTexel);
        _gl.Uniform1(_gl.GetUniformLocation(_preshadowProgram, "uTexelWorld"), p.ShadowTexelWorld);
        _gl.Uniform1(_gl.GetUniformLocation(_preshadowProgram, "uDepthRange"), p.ShadowDepthRange);
        targets.BindColorTarget(targets.PreShadow);
        BindTexture(_preshadowProgram, "tex_nld", 0, targets.LinearDepth.Handle);
        BindTexture(_preshadowProgram, "tex_shadow", 1, p.ShadowTexture);
        BindTexture(_preshadowProgram, "tex_gnrm", 2, targets.GBuffer[3].Handle);
        int count = p.Cascades?.ViewProj.Length ?? 0;
        _gl.Uniform1(_gl.GetUniformLocation(_preshadowProgram, "uCascadeCount"), count);
        _gl.ActiveTexture(TextureUnit.Texture3);
        _gl.BindTexture(TextureTarget.Texture2DArray, p.Cascades?.Texture ?? 0);
        _gl.Uniform1(_gl.GetUniformLocation(_preshadowProgram, "tex_cascades"), 3);
        _gl.ActiveTexture(TextureUnit.Texture0);
        if (p.Cascades is { } cascades)
        {
            _gl.Uniform1(_gl.GetUniformLocation(_preshadowProgram, "uCascadeTexel"), 1f / RenderTargets.CascadeSize);
            for (int c = 0; c < count; c++)
            {
                SetMat4(_preshadowProgram, $"uCascadeViewProj[{c}]", cascades.ViewProj[c]);
                // A wider filter on the finer cascades keeps their penumbra soft; the coarse ones
                // are soft by their texel size alone.
                float radius = c == 0 ? 3f : c == 1 ? 2f : 1.5f;
                _gl.Uniform4(_gl.GetUniformLocation(_preshadowProgram, $"uCascadeParams[{c}]"), cascades.TexelWorld[c], cascades.Bias[c], radius, 0f);
            }
        }
        resources.DrawFullscreenTriangle();

        // The game's two-tier preshading filter smooths PreShadow into a penumbra: rgb gets the half-res reduce blur and w the full-res 3-tap Gaussian (uStack_364 = 7).
        RunPreshadingFilter(resources, targets, targets.PreShadow, targets.AoTmp);

        // AO: raw into the AoRaw scratch (reused), then blurred into PreMisc.
        _gl.UseProgram(_aoProgram);
        _gl.Uniform2(_gl.GetUniformLocation(_aoProgram, "uTanHalf"), p.TanHalf.X, p.TanHalf.Y);
        _gl.Uniform2(_gl.GetUniformLocation(_aoProgram, "uPix"), 1f / targets.Width, 1f / targets.Height);
        _gl.Uniform1(_gl.GetUniformLocation(_aoProgram, "uNear"), p.Near);
        _gl.Uniform1(_gl.GetUniformLocation(_aoProgram, "uFar"), p.Far);
        _gl.Uniform1(_gl.GetUniformLocation(_aoProgram, "uRadius"), p.AoRadius);
        _gl.Uniform1(_gl.GetUniformLocation(_aoProgram, "uStrength"), p.AoStrength);
        _gl.Uniform3(_gl.GetUniformLocation(_aoProgram, "uSunView"), p.SunView.X, p.SunView.Y, p.SunView.Z);
        _gl.Uniform1(_gl.GetUniformLocation(_aoProgram, "uDebugNrm"), 0);
        targets.BindColorTarget(targets.AoRaw);
        BindTexture(_aoProgram, "tex_nld", 0, targets.LinearDepth.Handle);
        BindTexture(_aoProgram, "tex_gnrm", 3, targets.GBuffer[3].Handle);
        resources.DrawFullscreenTriangle();
        RunSeparableBlur(resources, targets, targets.AoRaw, targets.AoTmp, targets.PreMisc, p.Near, p.Far, p.AoRadius);
    }

    void RunPreshadingFilter(GLResourceCache resources, RenderTargets targets, GpuTexture preShadow, GpuTexture tmp)
    {
        _gl.UseProgram(_preshadingFilterProgram);

        // Tier 1: full-resolution anti-aliasing blur (agl_preshading_filter STEP1/STEP2), horizontal into tmp then vertical straight back into preShadow, for both rgb and w.
        targets.BindColorTarget(tmp);
        BindTexture(_preshadingFilterProgram, "tex_src", 0, preShadow.Handle);
        _gl.Uniform2(_gl.GetUniformLocation(_preshadingFilterProgram, "uStep"), 1.3636f / targets.Width, 0f);
        resources.DrawFullscreenTriangle();

        targets.BindColorTarget(preShadow);
        BindTexture(_preshadingFilterProgram, "tex_src", 0, tmp.Handle);
        _gl.Uniform2(_gl.GetUniformLocation(_preshadingFilterProgram, "uStep"), 0f, 1.3636f / targets.Height);
        resources.DrawFullscreenTriangle();

        // Tier 2: downsample to half resolution (TotK's Blur0/1 Reduce) with a hardware 2x2 bilinear downsample (STEP3).
        _gl.UseProgram(_preshadingUpsampleProgram);
        targets.BindColorTarget(targets.PreShadowHalf0);
        BindTexture(_preshadingUpsampleProgram, "tex_src", 0, preShadow.Handle);
        resources.DrawFullscreenTriangle();

        // Character blur at half-resolution: 2-tap average (STEP4/STEP5)
        _gl.UseProgram(_preshadingReduceProgram);
        int hw = targets.PreShadowHalf0.Width;
        int hh = targets.PreShadowHalf0.Height;

        targets.BindColorTarget(targets.PreShadowHalf1);
        BindTexture(_preshadingReduceProgram, "tex_src", 0, targets.PreShadowHalf0.Handle);
        _gl.Uniform2(_gl.GetUniformLocation(_preshadingReduceProgram, "uStep"), 1.0f / hw, 0f);
        resources.DrawFullscreenTriangle();

        targets.BindColorTarget(targets.PreShadowHalf0);
        BindTexture(_preshadingReduceProgram, "tex_src", 0, targets.PreShadowHalf1.Handle);
        _gl.Uniform2(_gl.GetUniformLocation(_preshadingReduceProgram, "uStep"), 0f, 1.0f / hh);
        resources.DrawFullscreenTriangle();

        // Upsample back to full resolution into dst (targets.PreShadow) with GL_LINEAR (STEP6). ColorMask(true, true, true, false) replicates TotK's uStack_364 = 7: the half-res blur goes into rgb while w keeps Tier 1's full-resolution Gaussian.
        _gl.UseProgram(_preshadingUpsampleProgram);
        targets.BindColorTarget(preShadow);
        BindTexture(_preshadingUpsampleProgram, "tex_src", 0, targets.PreShadowHalf0.Handle);
        _gl.ColorMask(true, true, true, false);
        resources.DrawFullscreenTriangle();
        _gl.ColorMask(true, true, true, true);
    }

    void RunSeparableBlur(GLResourceCache resources, RenderTargets targets, GpuTexture src, GpuTexture tmp, GpuTexture dst, float near, float far, float radius)
    {
        _gl.UseProgram(_blurProgram);
        _gl.Uniform1(_gl.GetUniformLocation(_blurProgram, "uNear"), near);
        _gl.Uniform1(_gl.GetUniformLocation(_blurProgram, "uFar"), far);
        _gl.Uniform1(_gl.GetUniformLocation(_blurProgram, "uRadius"), radius);

        (GpuTexture Src, GpuTexture Dst, Vector2 Step)[] steps =
        [
            (src, tmp, new Vector2(1f / targets.Width, 0f)),
            (tmp, dst, new Vector2(0f, 1f / targets.Height)),
        ];
        foreach (var (s, d, step) in steps)
        {
            targets.BindColorTarget(d);
            BindTexture(_blurProgram, "tex_ao", 0, s.Handle);
            BindTexture(_blurProgram, "tex_nld", 1, targets.LinearDepth.Handle);
            _gl.Uniform2(_gl.GetUniformLocation(_blurProgram, "uStep"), step.X, step.Y);
            resources.DrawFullscreenTriangle();
        }
    }

    void BindTexture(uint program, string uniform, int unit, uint textureHandle)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2D, textureHandle);
        _gl.Uniform1(_gl.GetUniformLocation(program, uniform), unit);
    }

    void SetMat4(uint program, string name, Vector4[] rows)
    {
        Span<float> flat = stackalloc float[16];
        for (int i = 0; i < 4; i++)
        {
            flat[i * 4 + 0] = rows[i].X;
            flat[i * 4 + 1] = rows[i].Y;
            flat[i * 4 + 2] = rows[i].Z;
            flat[i * 4 + 3] = rows[i].W;
        }
        _gl.UniformMatrix4(_gl.GetUniformLocation(program, name), 1, true, flat);
    }

    public void Dispose()
    {
        _gl.DeleteProgram(_preshadowProgram);
        _gl.DeleteProgram(_preshadingFilterProgram);
        _gl.DeleteProgram(_preshadingReduceProgram);
        _gl.DeleteProgram(_preshadingUpsampleProgram);
        _gl.DeleteProgram(_aoProgram);
        _gl.DeleteProgram(_blurProgram);
    }
}
