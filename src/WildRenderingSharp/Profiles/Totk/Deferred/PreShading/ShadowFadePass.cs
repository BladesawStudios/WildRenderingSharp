using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Shadows;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Deferred.PreShading;

/// <summary>
/// Fades the pre-shading's sun visibility to lit with distance, between the depths the game's shadow pre-pass authors, so the far cascades' coarse texels never
/// show on distant surfaces as hard-edged, speckled shadows.
/// </summary>
/// <remarks>The field program does not read the fade the environment block holds for it, so it is laid over the visibility it wrote.</remarks>
internal sealed class ShadowFadePass : IDisposable
{
    static readonly string FragmentSource = GlslFiles.Load("Totk/Deferred/PreShading/ShadowFade.frag");

    readonly GL _gl;
    readonly uint _program;

    public ShadowFadePass(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, FragmentSource, "preshading_shadow_fade");
    }

    public void Run(GLResourceCache resources, RenderTargets targets, float near, float far)
    {
        targets.BindColorTarget(targets.PreShadow);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        // Each channel goes the fade's share of the way to one, and only the two that hold the visibility change.
        _gl.BlendFuncSeparate(BlendingFactor.One, BlendingFactor.OneMinusSrcColor, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _gl.ColorMask(true, false, false, true);

        _gl.UseProgram(_program);
        _gl.BindTextureUniform(_program, "tex_depth", 0, targets.LinearDepthHalf.Handle);
        _gl.SetFloat(_program, "uNear", near);
        _gl.SetFloat(_program, "uFar", far);
        _gl.Uniform2(_gl.UniformLocation(_program, "uFade"), SunShadowFade.Start, SunShadowFade.End);
        resources.DrawFullscreenTriangle();

        _gl.ColorMask(true, true, true, true);
        _gl.Disable(EnableCap.Blend);
    }

    public void Dispose() => _gl.ReleaseProgram(_program);
}
