using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Pipeline.Passes;

/// <summary>
/// Draws one shape's silhouette as a flat, alpha-blended overlay on the finished frame - the Material Inspector's "which object is
/// this row" hover highlight.
/// </summary>
internal sealed class HighlightOverlayPass : IDisposable
{
    readonly GL _gl;
    readonly uint _program;

    static readonly string VertexSource = GlslFiles.Load("Pipeline/HighlightOverlay/Main.vert");

    static readonly string FragmentSource = GlslFiles.Load("Pipeline/HighlightOverlay/Main.frag");

    public HighlightOverlayPass(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, VertexSource, FragmentSource, "highlight_overlay");
    }

    public unsafe void Draw(GLResourceCache resources, RenderTargets targets, ActorDrawGroup owningActor, LoadedShape shape, Matrix4x4 mvp, Matrix4x4 viewProj, Vector4 color)
    {
        // A skinned shape reads its pose from _Mtx (binding 2), so this needs the owning actor's buffer rather than whichever was bound last.
        owningActor.BindUbos(resources);
        targets.BindColorTarget(targets.Ldr);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.Zero);
        _gl.BlendEquationSeparate(GLEnum.FuncAdd, GLEnum.FuncAdd);

        _gl.UseProgram(_program);
        _gl.SetMat4(_program, "uMVP", mvp);
        _gl.SetMat4(_program, "uViewProj", viewProj);
        _gl.SetVec4(_program, "uColor", color);
        _gl.Uniform1(_gl.UniformLocation(_program, "uSkinCount"), shape.VertexSkinCount);

        _gl.BindVertexArray(shape.PassIdVao);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)shape.IndexCount, DrawElementsType.UnsignedInt, null);

        _gl.Disable(EnableCap.Blend);
    }

    public void Dispose() => _gl.ReleaseProgram(_program);
}
