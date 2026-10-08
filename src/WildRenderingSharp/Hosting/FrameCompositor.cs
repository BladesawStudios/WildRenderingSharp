using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline;

namespace WildRenderingSharp.Hosting;

/// <summary>
/// Puts a <see cref="SceneView"/>'s output into a host's own framebuffer: copied, blended over the host's background, or written with its depth
/// into a scene the host has already drawn part of. Each method draws into whatever framebuffer is bound for drawing and leaves the state its own
/// description names.
/// </summary>
public sealed class FrameCompositor(GL gl) : IDisposable
{
    /// <summary>How a host's depth buffer is laid out; the library does not know whether it is reversed.</summary>
    /// <param name="Reversed">Whether depth runs near = 1, far = 0.</param>
    /// <param name="ClearDepth">What an empty depth buffer holds.</param>
    /// <param name="PassOrEqual">The test a fragment passes when it is in front of, or level with, what is there.</param>
    /// <param name="Pass">The ordinary in-front test, which the host expects restored afterwards.</param>
    public readonly record struct DepthLayout(bool Reversed, float ClearDepth, DepthFunction PassOrEqual, DepthFunction Pass);

    static readonly string Vertex = GlslFiles.Load("Pipeline/FrameCompositor/Vertex.vert");
    static readonly string OverFragment = GlslFiles.Load("Pipeline/FrameCompositor/Over.frag");
    static readonly string DepthFragment = GlslFiles.Load("Pipeline/FrameCompositor/Depth.frag");

    uint _overProgram, _depthProgram, _vao;

    /// <summary>Copies the view's output into the framebuffer bound for drawing, the right way up.</summary>
    public void Blit(SceneView view)
    {
        int read = gl.GetInteger(GetPName.ReadFramebufferBinding);
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, view.OutputFramebuffer);

        // The raw buffer views are stored top row first, the other way up from the finished frame.
        int top = view.OutputIsTopDown ? view.Height : 0, bottom = view.OutputIsTopDown ? 0 : view.Height;
        gl.BlitFramebuffer(0, 0, view.Width, view.Height, 0, top, view.Width, bottom, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (uint)read);
    }

    /// <summary>
    /// Clears the bound framebuffer to <paramref name="background"/> and blends the view's output over it. A transparent background leaves real
    /// coverage in the output's alpha; the shader takes it back out of the colour, so this is a plain source-alpha blend. Depth writes are left on.
    /// </summary>
    public void OverBackground(SceneView view, Vector3 background)
    {
        Ensure(ref _overProgram, OverFragment, "frame_over");

        gl.ClearColor(background.X, background.Y, background.Z, 1f);
        gl.Clear(ClearBufferMask.ColorBufferBit);

        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.CullFace);
        gl.DepthMask(false);
        gl.Enable(EnableCap.Blend);
        gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.Zero, BlendingFactor.One);

        gl.UseProgram(_overProgram);
        gl.Uniform1(gl.GetUniformLocation(_overProgram, "uImage"), 0);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, view.OutputTexture);
        DrawTriangle();
        gl.DepthMask(true);
    }

    /// <summary>
    /// Writes the view's colour and depth into the host's frame, depth-tested against what the host has drawn so far. The view's depth is a
    /// distance in its own planes and is turned back into the host's projection here. The sky is written at the far plane when the view drew one.
    /// </summary>
    /// <param name="near">The view's near plane.</param>
    /// <param name="far">The view's far plane.</param>
    /// <param name="hostProjection">The host's projection for this frame.</param>
    /// <param name="sky">Whether the view drew a sky, to be written where nothing else is.</param>
    public void WithDepth(SceneView view, in DepthLayout layout, float near, float far, Matrix4x4 hostProjection, bool sky)
    {
        Ensure(ref _depthProgram, DepthFragment, "frame_depth");

        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(layout.PassOrEqual);
        gl.DepthMask(true);
        gl.Disable(EnableCap.Blend);
        gl.Disable(EnableCap.CullFace);

        uint program = _depthProgram;
        gl.UseProgram(program);
        gl.SetInt(program, "uImage", 0);
        gl.SetInt(program, "uDepth", 1);
        gl.SetVec2(program, "uNearFar", new Vector2(near, far));
        gl.SetVec4(program, "uProjDepth", new Vector4(hostProjection.M33, hostProjection.M43, hostProjection.M34, hostProjection.M44));
        gl.SetInt(program, "uReversed", layout.Reversed ? 1 : 0);
        gl.SetInt(program, "uSky", sky ? 1 : 0);
        gl.SetInt(program, "uFlip", view.OutputIsTopDown ? 1 : 0);
        gl.SetFloat(program, "uClear", layout.ClearDepth);

        // The host keeps its own textures on these units across passes, so they are put back afterwards.
        gl.GetInteger(GetPName.ActiveTexture, out int active);
        gl.ActiveTexture(TextureUnit.Texture1);
        gl.GetInteger(GetPName.TextureBinding2D, out int was1);
        gl.BindTexture(TextureTarget.Texture2D, view.DepthTexture);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.GetInteger(GetPName.TextureBinding2D, out int was0);
        gl.BindTexture(TextureTarget.Texture2D, view.OutputTexture);
        DrawTriangle();
        gl.BindTexture(TextureTarget.Texture2D, (uint)was0);
        gl.ActiveTexture(TextureUnit.Texture1);
        gl.BindTexture(TextureTarget.Texture2D, (uint)was1);
        gl.ActiveTexture((TextureUnit)active);

        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.DepthFunc(layout.Pass);
    }

    void Ensure(ref uint program, string fragment, string name)
    {
        if (program != 0)
            return;
        program = GLProgramBuilder.Build(gl, Vertex, fragment, name);
        if (_vao == 0)
            _vao = gl.GenVertexArray();
    }

    void DrawTriangle()
    {
        gl.BindVertexArray(_vao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (_overProgram != 0) gl.DeleteProgram(_overProgram);
        if (_depthProgram != 0) gl.DeleteProgram(_depthProgram);
        if (_vao != 0) gl.DeleteVertexArray(_vao);
        _overProgram = _depthProgram = _vao = 0;
    }
}
