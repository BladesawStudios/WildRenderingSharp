using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline.Passes;

/// <summary>Builds <c>cTex_NormalizedLinearDepth</c>, <c>(viewZ - near) / (far - near)</c>, from the G-buffer depth at full and half resolution.</summary>
internal sealed class LinearDepthPass : IDisposable
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
        _gl.Uniform1(_gl.UniformLocation(_fullProgram, "tex_depth"), 0);
        _gl.Uniform1(_gl.UniformLocation(_fullProgram, "uNear"), near);
        _gl.Uniform1(_gl.UniformLocation(_fullProgram, "uFar"), far);
        resources.DrawFullscreenTriangle();

        _gl.UseProgram(_halfProgram);
        targets.BindColorTarget(targets.LinearDepthHalf);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, targets.LinearDepth.Handle);
        _gl.Uniform1(_gl.UniformLocation(_halfProgram, "tex_nld"), 0);
        resources.DrawFullscreenTriangle();
    }

    public void Dispose()
    {
        _gl.ReleaseProgram(_fullProgram);
        _gl.ReleaseProgram(_halfProgram);
    }
}
