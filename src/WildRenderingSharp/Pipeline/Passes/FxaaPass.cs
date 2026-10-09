using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Shaders;
using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline.Passes;

/// <summary>A simplified single-pass FXAA-style edge-aware blur, the anti-aliasing used instead of supersampling by default.</summary>
public sealed class FxaaPass : IDisposable
{
    readonly GL _gl;
    readonly uint _program;

    static readonly string FragmentSource = GlslFiles.Load("Pipeline/Fxaa/Main.frag");

    public FxaaPass(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, FragmentSource, "fxaa");
    }

    public void Run(GLResourceCache resources, GpuTexture source)
    {
        _gl.UseProgram(_program);
        _gl.BindTextureUniform(_program, "t", 0, source.Handle);
        _gl.SetVec2(_program, "uTexel", new Vector2(1f / source.Width, 1f / source.Height));
        resources.DrawFullscreenTriangle();
    }

    public void Dispose() => _gl.DeleteProgram(_program);
}
