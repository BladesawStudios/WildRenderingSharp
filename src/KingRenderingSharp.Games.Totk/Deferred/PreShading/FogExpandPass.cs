using Silk.NET.OpenGL;
using KingRenderingSharp.Gpu;
using KingRenderingSharp.Graph.Pipeline.Passes;
using KingRenderingSharp.Graph.Pipeline.Resources;
using KingRenderingSharp.Graph.Pipeline.Targets;
using KingRenderingSharp.Content.Shaders;

namespace KingRenderingSharp.Games.Totk.Deferred.PreShading;

/// <summary>
/// Spreads the pre-shading's half-resolution fog to the frame's resolution, each pixel taking the fog of the texel nearest its own depth. The resolves
/// read it with a plain bilinear fetch, which at half resolution puts the fog of one surface on the pixels at the edge of another.
/// </summary>
internal sealed class FogExpandPass : IDisposable
{
    static readonly string FragmentSource = GlslFiles.Load("Totk/Deferred/PreShading/FogExpand.frag");

    readonly GL _gl;
    readonly uint _program;

    public FogExpandPass(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, FragmentSource, "preshading_fog_expand");
    }

    public void Run(GLResourceCache resources, RenderTargets targets)
    {
        _gl.UseProgram(_program);
        targets.BindColorTarget(targets.PreFog);
        _gl.BindTextureUniform(_program, "tex_fog", 0, targets.PreFogHalf.Handle);
        _gl.BindTextureUniform(_program, "tex_depth", 1, targets.LinearDepth.Handle);
        _gl.BindTextureUniform(_program, "tex_half_depth", 2, targets.LinearDepthHalf.Handle);
        resources.DrawFullscreenTriangle();
    }

    public void Dispose() => _gl.ReleaseProgram(_program);
}
