using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Shadows;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Pipeline.Passes;

/// <summary>
/// Produces the two screen-space buffers the deferred resolve shaders expect from the game's <c>preshading_*</c> passes: <c>cTex_PreShadow</c>
/// (sun visibility, Poisson-disc PCF against the shadow map) and <c>cTex_PreMisc</c> (an alchemy-style AO plus the diffuse N.L the <c>chara_*</c> passes read).
/// </summary>
internal sealed class ScreenSpaceShadowAndAoPass : IDisposable
{
    public const float AoRadiusWorld = 0.035f;
    public const float AoStrength = 0.85f;
    public const float ShadowBiasWorld = 0.0015f;

    // The 1.3636 offset is a linear-sampling tap that folds two Gaussian taps into one fetch.
    const float FilterTapOffset = 1.3636f;

    static readonly string PreshadowFragmentSource = GlslFiles.Load("Pipeline/ScreenSpaceShadowAndAo/Preshadow.frag");
    static readonly string AoFragmentSource = GlslFiles.Load("Pipeline/ScreenSpaceShadowAndAo/Ao.frag");
    static readonly string BlurFragmentSource = GlslFiles.Load("Pipeline/ScreenSpaceShadowAndAo/Blur.frag");
    static readonly string PreshadingFilterFragmentSource = GlslFiles.Load("Pipeline/ScreenSpaceShadowAndAo/PreshadingFilter.frag");

    public readonly record struct Params(
        Matrix4x4 ViewInv, Matrix4x4 LightViewProj, Vector2 TanHalf, Vector3 SunWorld, Vector3 SunView,
        float Near, float Far, float ShadowBias, float ShadowTexel, float ShadowTexelWorld, float ShadowDepthRange,
        float AoRadius, float AoStrength, uint ShadowTexture,
        CascadeParams? Cascades = null);

    // The cascades a frame's shadows come from - see ShadowCascades.
    public sealed record CascadeParams(uint Texture, Matrix4x4[] ViewProj, float[] TexelWorld, float[] Bias, float[] InvDepthRange);

    readonly GL _gl;
    readonly uint _preshadowProgram, _preshadingFilterProgram, _aoProgram, _blurProgram;

    public ScreenSpaceShadowAndAoPass(GL gl)
    {
        _gl = gl;
        _preshadowProgram = Build("preshadow", PreshadowFragmentSource);
        _preshadingFilterProgram = Build("preshading_filter", PreshadingFilterFragmentSource);
        _aoProgram = Build("ao", AoFragmentSource);
        _blurProgram = Build("ao_blur", BlurFragmentSource);
    }

    public void Run(GLResourceCache resources, RenderTargets targets, Params p)
    {
        _gl.Disable(EnableCap.DepthTest);

        DrawPreShadow(resources, targets, p);
        DrawAo(resources, targets, p);
        RunSeparableBlur(resources, targets, targets.AoRaw, targets.AoTmp, targets.PreMisc, p.Near, p.Far, p.AoRadius);
    }

    // The game's filter smooths PreShadow with one separable 3-tap Gaussian, as its shadow pre-pass is configured (a blur of width 1, repeated once).
    public void FilterPreShadow(GLResourceCache resources, RenderTargets targets)
    {
        _gl.Disable(EnableCap.DepthTest);
        BlurPreShadow(resources, targets, targets.PreShadow, targets.PreShadowScratch);
    }

    public void Dispose()
    {
        foreach (uint program in new[] { _preshadowProgram, _preshadingFilterProgram, _aoProgram, _blurProgram })
            _gl.ReleaseProgram(program);
    }

    uint Build(string name, string fragmentSource) => GLProgramBuilder.Build(_gl, FullscreenShaders.Vertex450, fragmentSource, name);

    // Raw PCF visibility against the shadow map and the cascades.
    void DrawPreShadow(GLResourceCache resources, RenderTargets targets, Params p)
    {
        uint program = _preshadowProgram;
        _gl.UseProgram(program);
        _gl.SetMat4(program, "uViewInv", p.ViewInv);
        _gl.SetMat4(program, "uLightViewProj", p.LightViewProj);
        _gl.SetVec2(program, "uTanHalf", p.TanHalf);
        _gl.SetVec3(program, "uSunWorld", p.SunWorld);
        _gl.SetFloat(program, "uNear", p.Near);
        _gl.SetFloat(program, "uFar", p.Far);
        _gl.SetFloat(program, "uBias", p.ShadowBias);
        _gl.SetFloat(program, "uTexel", p.ShadowTexel);
        _gl.SetFloat(program, "uTexelWorld", p.ShadowTexelWorld);
        _gl.SetFloat(program, "uDepthRange", p.ShadowDepthRange);
        _gl.SetVec2(program, "uPix", new Vector2(1f / targets.Width, 1f / targets.Height));
        _gl.SetVec2(program, "uFarFade", new Vector2(SunShadowFade.Start, SunShadowFade.End));
        targets.BindColorTarget(targets.PreShadow);
        _gl.BindTextureUniform(program, "tex_nld", 0, targets.LinearDepth.Handle);
        _gl.BindTextureUniform(program, "tex_shadow", 1, p.ShadowTexture);
        _gl.BindTextureUniform(program, "tex_gnrm", 2, targets.GBuffer[3].Handle);
        SetCascades(program, p.Cascades);
        resources.DrawFullscreenTriangle();
    }

    void SetCascades(uint program, CascadeParams? cascades)
    {
        int count = cascades?.ViewProj.Length ?? 0;
        _gl.SetInt(program, "uCascadeCount", count);
        _gl.BindTextureUniform(program, "tex_cascades", 3, cascades?.Texture ?? 0, TextureTarget.Texture2DArray);
        _gl.ActiveTexture(TextureUnit.Texture0);
        if (cascades is null)
            return;

        _gl.SetFloat(program, "uCascadeTexel", 1f / RenderTargets.CascadeSize);
        for (int c = 0; c < count; c++)
        {
            _gl.SetMat4(program, $"uCascadeViewProj[{c}]", cascades.ViewProj[c]);
            // A wider filter on the finer cascades keeps their penumbra soft; the coarse ones are soft by their texel size alone.
            float radius = c == 0 ? 3f : c == 1 ? 2f : 1.5f;
            _gl.SetVec4(program, $"uCascadeParams[{c}]", new Vector4(cascades.TexelWorld[c], cascades.Bias[c], radius, cascades.InvDepthRange[c]));
        }
    }

    // Raw AO into the AoRaw scratch, which the separable blur then smooths into PreMisc.
    void DrawAo(GLResourceCache resources, RenderTargets targets, Params p)
    {
        _gl.UseProgram(_aoProgram);
        _gl.SetVec2(_aoProgram, "uTanHalf", p.TanHalf);
        _gl.SetVec2(_aoProgram, "uPix", new Vector2(1f / targets.Width, 1f / targets.Height));
        _gl.SetFloat(_aoProgram, "uNear", p.Near);
        _gl.SetFloat(_aoProgram, "uFar", p.Far);
        _gl.SetFloat(_aoProgram, "uRadius", p.AoRadius);
        _gl.SetFloat(_aoProgram, "uStrength", p.AoStrength);
        _gl.SetVec3(_aoProgram, "uSunView", p.SunView);
        _gl.SetInt(_aoProgram, "uDebugNrm", 0);
        targets.BindColorTarget(targets.AoRaw);
        _gl.BindTextureUniform(_aoProgram, "tex_nld", 0, targets.LinearDepth.Handle);
        _gl.BindTextureUniform(_aoProgram, "tex_gnrm", 3, targets.GBuffer[3].Handle);
        resources.DrawFullscreenTriangle();
    }

    // Horizontal into the scratch target and vertical straight back.
    void BlurPreShadow(GLResourceCache resources, RenderTargets targets, GpuTexture preShadow, GpuTexture tmp)
    {
        _gl.UseProgram(_preshadingFilterProgram);
        targets.BindColorTarget(tmp);
        _gl.BindTextureUniform(_preshadingFilterProgram, "tex_src", 0, preShadow.Handle);
        _gl.BindTextureUniform(_preshadingFilterProgram, "tex_depth", 1, targets.LinearDepthHalf.Handle);
        _gl.SetVec2(_preshadingFilterProgram, "uStep", new Vector2(FilterTapOffset / preShadow.Width, 0f));
        resources.DrawFullscreenTriangle();

        targets.BindColorTarget(preShadow);
        _gl.BindTextureUniform(_preshadingFilterProgram, "tex_src", 0, tmp.Handle);
        _gl.SetVec2(_preshadingFilterProgram, "uStep", new Vector2(0f, FilterTapOffset / preShadow.Height));
        resources.DrawFullscreenTriangle();
    }

    void RunSeparableBlur(GLResourceCache resources, RenderTargets targets, GpuTexture src, GpuTexture tmp, GpuTexture dst, float near, float far, float radius)
    {
        _gl.UseProgram(_blurProgram);
        _gl.SetFloat(_blurProgram, "uNear", near);
        _gl.SetFloat(_blurProgram, "uFar", far);
        _gl.SetFloat(_blurProgram, "uRadius", radius);

        BlurStep(resources, targets, src, tmp, new Vector2(1f / targets.Width, 0f));
        BlurStep(resources, targets, tmp, dst, new Vector2(0f, 1f / targets.Height));
    }

    void BlurStep(GLResourceCache resources, RenderTargets targets, GpuTexture source, GpuTexture destination, Vector2 step)
    {
        targets.BindColorTarget(destination);
        _gl.BindTextureUniform(_blurProgram, "tex_ao", 0, source.Handle);
        _gl.BindTextureUniform(_blurProgram, "tex_nld", 1, targets.LinearDepth.Handle);
        _gl.SetVec2(_blurProgram, "uStep", step);
        resources.DrawFullscreenTriangle();
    }
}
