using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Pipeline.Passes;

/// <summary>Copies one colour target into another, optionally flipped vertically, as the frame moves between the G-buffer's orientation and the output's.</summary>
internal sealed class FlipBlit : IDisposable
{
    static readonly string FlipFragmentSource = GlslFiles.Load("Pipeline/Forward/Flip.frag");

    // The combine step after forward drawing: the scene back in the frame's units, keeping the coverage the deferred pass wrote.
    static readonly string CoverageFragmentSource = GlslFiles.Load("Pipeline/Forward/Coverage.frag");

    readonly GL _gl;
    readonly uint _flipProgram, _coverageProgram;

    public FlipBlit(GL gl)
    {
        _gl = gl;
        _flipProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, FlipFragmentSource, "flip_blit");
        _coverageProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, CoverageFragmentSource, "flip_blit_coverage");
    }

    public void Copy(GLResourceCache resources, RenderTargets targets, GpuTexture dst, GpuTexture src, bool flip, float scale = 1f)
    {
        _gl.UseProgram(_flipProgram);
        targets.BindColorTarget(dst);
        _gl.BindTextureUniform(_flipProgram, "t", 0, src.Handle);
        _gl.SetInt(_flipProgram, "uFlip", flip ? 1 : 0);
        _gl.SetFloat(_flipProgram, "uScale", scale);
        resources.DrawFullscreenTriangle();
    }

    public void CopyKeepingCoverage(GLResourceCache resources, RenderTargets targets, GpuTexture dst, GpuTexture src, GpuTexture coverage, bool flip, float scale = 1f)
    {
        _gl.UseProgram(_coverageProgram);
        targets.BindColorTarget(dst);
        _gl.BindTextureUniform(_coverageProgram, "t", 0, src.Handle);
        _gl.BindTextureUniform(_coverageProgram, "tCoverage", 1, coverage.Handle);
        _gl.SetInt(_coverageProgram, "uFlip", flip ? 1 : 0);
        _gl.SetFloat(_coverageProgram, "uScale", scale);
        resources.DrawFullscreenTriangle();
    }

    public void Dispose()
    {
        _gl.ReleaseProgram(_flipProgram);
        _gl.ReleaseProgram(_coverageProgram);
    }
}
