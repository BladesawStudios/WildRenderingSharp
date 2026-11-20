using KingRenderingSharp.Gpu;
using KingRenderingSharp.Graph.Pipeline.Resources;
using KingRenderingSharp.Content.Shaders;
using System.Numerics;

namespace KingRenderingSharp.Graph.Pipeline.Passes;

/// <summary>A simplified single-pass FXAA-style edge-aware blur, the anti-aliasing used instead of supersampling by default.</summary>
internal sealed class FxaaPass : IDisposable
{
    readonly GpuApi _gl;
    readonly uint _program;

    static readonly string FragmentSource = GlslFiles.Load("Pipeline/Fxaa/Main.frag");

    public FxaaPass(GpuApi gl)
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

    public void Dispose() => _gl.ReleaseProgram(_program);
}
