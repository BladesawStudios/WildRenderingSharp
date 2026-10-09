using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline.Gpu;

namespace WildRenderingSharp.Hosting.Views;

/// <summary>The RGBA8 texture a view hands the host, and the same-sized intermediate the graded views are composed in before anti-aliasing.</summary>
sealed class ViewOutput(GL gl) : IDisposable
{
    readonly uint _framebuffer = gl.GenFramebuffer(), _gradedFramebuffer = gl.GenFramebuffer();
    uint _gradedTexture;
    int _gradedWidth, _gradedHeight;

    public uint Texture { get; private set; }

    public uint Framebuffer => _framebuffer;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public GpuTexture Graded => new(_gradedTexture, Width, Height);

    // Recreates whichever texture no longer matches the size.
    public void Ensure(int width, int height)
    {
        if (width != Width || height != Height || Texture == 0)
        {
            Replace(Texture, out uint texture, width, height, _framebuffer);
            (Texture, Width, Height) = (texture, width, height);
        }
        if (width != _gradedWidth || height != _gradedHeight || _gradedTexture == 0)
        {
            Replace(_gradedTexture, out _gradedTexture, width, height, _gradedFramebuffer);
            (_gradedWidth, _gradedHeight) = (width, height);
        }
    }

    // Binds the framebuffer a view draws into with a clean raster state.
    public void Bind(bool graded)
    {
        BindFramebuffer(graded ? _gradedFramebuffer : _framebuffer);
        gl.Disable(EnableCap.Blend);
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.ScissorTest);
    }

    public void BindFinal() => BindFramebuffer(_framebuffer);

    // Rows come back bottom-up unless the view already draws top-down.
    public unsafe byte[] ReadRgba8(bool topDown)
    {
        var raw = new byte[Width * Height * 4];
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        gl.PixelStore(PixelStoreParameter.PackAlignment, 4);
        fixed (byte* p = raw)
            gl.ReadPixels(0, 0, (uint)Width, (uint)Height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        if (topDown)
            return raw;

        var flipped = new byte[raw.Length];
        int stride = Width * 4;
        for (int y = 0; y < Height; y++)
            Array.Copy(raw, y * stride, flipped, (Height - 1 - y) * stride, stride);
        return flipped;
    }

    public void Dispose()
    {
        if (Texture != 0)
            gl.DeleteTexture(Texture);
        if (_gradedTexture != 0)
            gl.DeleteTexture(_gradedTexture);
        gl.DeleteFramebuffer(_framebuffer);
        gl.DeleteFramebuffer(_gradedFramebuffer);
    }

    void BindFramebuffer(uint framebuffer)
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    unsafe void Replace(uint old, out uint texture, int width, int height, uint framebuffer)
    {
        if (old != 0)
            gl.DeleteTexture(old);
        texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        gl.SetSampling(TextureTarget.Texture2D, GLEnum.Linear, GLEnum.ClampToEdge);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
    }
}
