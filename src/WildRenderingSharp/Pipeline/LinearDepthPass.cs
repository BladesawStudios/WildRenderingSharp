using WildRenderingSharp.Graphics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Builds <c>cTex_NormalizedLinearDepth</c> - <c>(viewZ - near) / (far - near)</c> - at full resolution from the G-buffer's
/// hardware depth, then a half-resolution copy via a 4-tap <c>textureGather</c> min (matching
/// <c>prog_nld</c>/<c>prog_nld_half</c>).
/// </summary>
public sealed class LinearDepthPass : IDisposable
{
    readonly GL _gl;
    readonly uint _fullProgram, _halfProgram;

    static readonly string FullFragmentSource = GlslFiles.Load("Pipeline/LinearDepth/Full.frag");

    static readonly string HalfFragmentSource = GlslFiles.Load("Pipeline/LinearDepth/Half.frag");

    public LinearDepthPass(GL gl)
    {
        _gl = gl;
        _fullProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, FullFragmentSource, "linear_depth_full");
        _halfProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, HalfFragmentSource, "linear_depth_half");
    }

    public void Run(GLResourceCache resources, RenderTargets targets, float near, float far)
    {
        _gl.Disable(EnableCap.DepthTest);

        _gl.UseProgram(_fullProgram);
        targets.BindColorTarget(targets.LinearDepth);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, targets.GBufferDepth.Handle);
        _gl.Uniform1(_gl.GetUniformLocation(_fullProgram, "tex_depth"), 0);
        _gl.Uniform1(_gl.GetUniformLocation(_fullProgram, "uNear"), near);
        _gl.Uniform1(_gl.GetUniformLocation(_fullProgram, "uFar"), far);
        resources.DrawFullscreenTriangle();

        _gl.UseProgram(_halfProgram);
        targets.BindColorTarget(targets.LinearDepthHalf);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, targets.LinearDepth.Handle);
        _gl.Uniform1(_gl.GetUniformLocation(_halfProgram, "tex_nld"), 0);
        resources.DrawFullscreenTriangle();
    }

    public void Dispose()
    {
        _gl.DeleteProgram(_fullProgram);
        _gl.DeleteProgram(_halfProgram);
    }
}
