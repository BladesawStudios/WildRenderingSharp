using System.Numerics;
using Silk.NET.OpenGL;
using static WildRenderingSharp.Profiles.Totk.Sky.Precompute.SkyPrecomputePass;

namespace WildRenderingSharp.Profiles.Totk.Sky.Precompute;

/// <summary>The framebuffer and quad the sky precompute chain draws with: it targets a texture or a layer of one, binds inputs, and draws a program over the target.</summary>
sealed unsafe class SkyCanvas : IDisposable
{
    readonly GL _gl;
    readonly SkyChainPrograms _programs;
    readonly uint _framebuffer, _vao, _vbo;

    public SkyCanvas(GL gl, SkyChainPrograms programs)
    {
        _gl = gl;
        _programs = programs;

        Vector4[] quad = [new(-0.5f, -0.5f, 0f, 1f), new(0.5f, -0.5f, 0f, 1f), new(-0.5f, 0.5f, 0f, 1f), new(0.5f, 0.5f, 0f, 1f)];
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (Vector4* p = quad)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * sizeof(Vector4)), p, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 4, VertexAttribPointerType.Float, false, (uint)sizeof(Vector4), (void*)0);
        gl.BindVertexArray(0);

        _framebuffer = gl.GenFramebuffer();
    }

    public void Target2D(uint texture, int width, int height)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
        DrawBuffers(1);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
    }

    public void TargetLayer(uint texture, int layer)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, texture, 0, layer);
        DrawBuffers(1);
        _gl.Viewport(0, 0, InscatterW, InscatterH);
    }

    public void TargetLayerPair(uint first, uint second, int layer)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, first, 0, layer);
        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, second, 0, layer);
        DrawBuffers(2);
        _gl.Viewport(0, 0, InscatterW, InscatterH);
    }

    public void BindInput(int unit, TextureTarget target, uint texture)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(target, texture);
    }

    public void Clear()
    {
        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    public void Draw(string program)
    {
        _gl.UseProgram(_programs[program]);
        Draw();
    }

    // Draws the quad with whichever program is in use.
    public void Draw()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        _gl.BindVertexArray(0);
    }

    void DrawBuffers(int count)
    {
        Span<GLEnum> buffers = [GLEnum.ColorAttachment0, GLEnum.ColorAttachment1];
        fixed (GLEnum* p = buffers)
            _gl.DrawBuffers((uint)count, p);
        if (count < 2)
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, TextureTarget.Texture2D, 0, 0);
    }

    public void Dispose()
    {
        _gl.DeleteFramebuffer(_framebuffer);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
