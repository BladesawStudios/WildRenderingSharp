using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline.Gpu;

/// <summary>Reads a colour texture back to the CPU through a framebuffer of its own.</summary>
sealed unsafe class PixelReadback : IDisposable
{
    readonly GL _gl;
    readonly uint _framebuffer;

    public PixelReadback(GL gl)
    {
        _gl = gl;
        _framebuffer = gl.GenFramebuffer();
    }

    public Vector4 Pixel(GpuTexture target, int x, int y)
    {
        Attach(target);
        float* pixel = stackalloc float[4];
        _gl.ReadPixels(Math.Clamp(x, 0, target.Width - 1), Math.Clamp(y, 0, target.Height - 1), 1, 1, PixelFormat.Rgba, PixelType.Float, pixel);
        return new Vector4(pixel[0], pixel[1], pixel[2], pixel[3]);
    }

    // Rows are returned top-down, the reverse of GL's bottom-up order.
    public byte[] Rgba8(GpuTexture target)
    {
        Attach(target);
        var raw = new byte[target.Width * target.Height * 4];
        fixed (byte* p = raw)
            _gl.ReadPixels(0, 0, (uint)target.Width, (uint)target.Height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        return FlipRows(raw, target.Width * 4);
    }

    public float[] FloatRgba(GpuTexture target)
    {
        Attach(target);
        var raw = new float[target.Width * target.Height * 4];
        fixed (float* p = raw)
            _gl.ReadPixels(0, 0, (uint)target.Width, (uint)target.Height, PixelFormat.Rgba, PixelType.Float, p);
        return FlipRows(raw, target.Width * 4);
    }

    public void Dispose() => _gl.DeleteFramebuffer(_framebuffer);

    void Attach(GpuTexture target)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, target.Handle, 0);
    }

    static T[] FlipRows<T>(T[] raw, int stride)
    {
        var flipped = new T[raw.Length];
        int rows = raw.Length / stride;
        for (int y = 0; y < rows; y++)
            Array.Copy(raw, y * stride, flipped, (rows - 1 - y) * stride, stride);
        return flipped;
    }
}
