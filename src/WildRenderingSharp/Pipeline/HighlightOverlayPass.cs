using WildRenderingSharp.Graphics;
using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Draws one shape's silhouette as a flat, alpha-blended overlay on the finished frame - the Material Inspector's "which object is
/// this row" hover highlight.
/// </summary>
public sealed class HighlightOverlayPass : IDisposable
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
        // A skinned highlighted shape reads its bone pose from _Mtx (binding 2) exactly like every
        // other pass - has to be THIS shape's own actor's buffer, not whichever one happened to be
        // bound last (that's why this takes the owning ActorDrawGroup, not just the shape).
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
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uSkinCount"), shape.VertexSkinCount);

        _gl.BindVertexArray(shape.PassIdVao);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)shape.IndexCount, DrawElementsType.UnsignedInt, null);

        _gl.Disable(EnableCap.Blend);
    }

    public void Dispose() => _gl.DeleteProgram(_program);
}
