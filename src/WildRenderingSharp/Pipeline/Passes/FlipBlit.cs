using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Pipeline.Passes;

/// <summary>Copies one colour target into another, optionally flipped vertically, as the frame moves between the G-buffer's orientation and the output's.</summary>
public sealed class FlipBlit : IDisposable
{
    static readonly string FlipFragmentSource = GlslFiles.Load("Pipeline/Forward/Flip.frag");

    // The combine step after forward geometry has been added into the scene. The forward program's base colour term is negative for
    // every input (a decompiler artifact), so the result is floored at the deferred-only copy to let the forward pass only brighten.
    static readonly string FloorFragmentSource = GlslFiles.Load("Pipeline/Forward/Floor.frag");

    readonly GL _gl;
    readonly uint _flipProgram, _floorProgram;

    public FlipBlit(GL gl)
    {
        _gl = gl;
        _flipProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, FlipFragmentSource, "flip_blit");
        _floorProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, FloorFragmentSource, "flip_blit_floor");
    }

    public void Copy(GLResourceCache resources, RenderTargets targets, GpuTexture dst, GpuTexture src, bool flip)
    {
        _gl.UseProgram(_flipProgram);
        targets.BindColorTarget(dst);
        _gl.BindTextureUniform(_flipProgram, "t", 0, src.Handle);
        _gl.SetInt(_flipProgram, "uFlip", flip ? 1 : 0);
        resources.DrawFullscreenTriangle();
    }

    public void CopyWithFloor(GLResourceCache resources, RenderTargets targets, GpuTexture dst, GpuTexture src, GpuTexture floor, bool flip)
    {
        _gl.UseProgram(_floorProgram);
        targets.BindColorTarget(dst);
        _gl.BindTextureUniform(_floorProgram, "t", 0, src.Handle);
        _gl.BindTextureUniform(_floorProgram, "tFloor", 1, floor.Handle);
        _gl.SetInt(_floorProgram, "uFlip", flip ? 1 : 0);
        resources.DrawFullscreenTriangle();
    }

    public void Dispose()
    {
        _gl.DeleteProgram(_flipProgram);
        _gl.DeleteProgram(_floorProgram);
    }
}
