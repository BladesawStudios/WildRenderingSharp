using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>A screen-sized texture a pass of the profile's own draws into or keeps a copy in, with its framebuffer when it is drawn into.</summary>
/// <remarks>
/// Made on first use and made again when the screen's size changes; sampled by texel, nearest. A depth format has no framebuffer of its own: it is copied
/// into, or drawn into as another's depth (<see cref="AttachDepth"/>).
/// </remarks>
internal sealed class ScreenTarget(GL gl, InternalFormat format) : IDisposable
{
    uint _framebuffer;
    int _width, _height;
    bool _hasDepth;

    public uint Texture { get; private set; }

    bool IsDepth => format is InternalFormat.DepthComponent32f or InternalFormat.DepthComponent24 or InternalFormat.DepthComponent16;

    public void Ensure(int width, int height)
    {
        if (Texture != 0 && width == _width && height == _height)
            return;
        Dispose();
        (_width, _height) = (width, height);
        Texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, Texture);
        gl.TexStorage2D(TextureTarget.Texture2D, 1, (SizedInternalFormat)format, (uint)width, (uint)height);
        gl.SetSampling(TextureTarget.Texture2D, GLEnum.Nearest, GLEnum.ClampToEdge);
        if (IsDepth)
            return;
        _framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, Texture, 0);
        FramebufferCheck.Validate(gl, "screen target");
    }

    // Gives the framebuffer a depth target of the same size, made first; called again whenever either is made again.
    public void AttachDepth(ScreenTarget depth)
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, depth.Texture, 0);
        FramebufferCheck.Validate(gl, "screen target with depth");
        _hasDepth = true;
    }

    // Draws into it, cleared to the value given, and its depth, if it has one, to the far plane.
    public void BindCleared(float clear)
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        gl.Viewport(0, 0, (uint)_width, (uint)_height);
        gl.ColorMask(true, true, true, true);
        gl.ClearColor(clear, clear, clear, clear);
        if (!_hasDepth)
        {
            gl.Clear(ClearBufferMask.ColorBufferBit);
            return;
        }
        gl.DepthMask(true);
        gl.ClearDepth(1.0);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    // Copies a texture of the same size and format into it.
    public void CopyFrom(uint source) =>
        gl.CopyImageSubData(source, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0, Texture, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0,
            (uint)_width, (uint)_height, 1);

    public void Dispose()
    {
        if (_framebuffer != 0)
            gl.DeleteFramebuffer(_framebuffer);
        if (Texture != 0)
            gl.DeleteTexture(Texture);
        _framebuffer = 0;
        Texture = 0;
        _hasDepth = false;
    }
}
