using Silk.NET.OpenGL;

namespace WildRenderingSharp.Profiles.Botw.Stages;

/// <summary>The buffers the pre-shading passes write, as attachments of one framebuffer, since each pass writes to the locations the game's engine gave it.</summary>
public sealed class PreShadingTargets(GL gl) : IDisposable
{
    public const int Count = 6;

    readonly uint[] _textures = new uint[Count];
    uint _framebuffer, _array;
    int _width, _height;

    public uint Texture(int attachment) => _textures[attachment];

    /// <summary>A one-layer array holding a copy of an attachment, for the passes that read a pre-shading buffer as an array.</summary>
    public uint Array => _array;

    public unsafe void Ensure(int width, int height)
    {
        if (width == _width && height == _height)
            return;
        Release();
        _width = width;
        _height = height;

        _framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        for (int i = 0; i < Count; i++)
        {
            _textures[i] = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, _textures[i]);
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.Float, null);
            SetSampling(TextureTarget.Texture2D, _textures[i]);
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0 + i, TextureTarget.Texture2D, _textures[i], 0);
        }

        _array = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2DArray, _array);
        gl.TexImage3D(TextureTarget.Texture2DArray, 0, InternalFormat.Rgba16f, (uint)width, (uint)height, 1, 0, PixelFormat.Rgba, PixelType.Float, null);
        SetSampling(TextureTarget.Texture2DArray, _array);
    }

    /// <summary>Draws into these attachments only; the rest are switched off, so a pass cannot write where it should not.</summary>
    public void Bind(params int[] attachments)
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        var buffers = new GLEnum[Count];
        for (int i = 0; i < Count; i++)
            buffers[i] = System.Array.IndexOf(attachments, i) >= 0 ? GLEnum.ColorAttachment0 + i : GLEnum.None;
        gl.DrawBuffers(buffers);
        gl.Viewport(0, 0, (uint)_width, (uint)_height);
    }

    public void Clear(int attachment, float r, float g, float b, float a)
    {
        Bind(attachment);
        gl.ClearColor(r, g, b, a);
        gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    public void CopyToArray(int attachment) =>
        gl.CopyImageSubData(_textures[attachment], CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0,
            _array, CopyImageSubDataTarget.Texture2DArray, 0, 0, 0, 0, (uint)_width, (uint)_height, 1);

    void SetSampling(TextureTarget target, uint texture)
    {
        gl.TexParameter(target, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(target, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        gl.TexParameter(target, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(target, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
    }

    void Release()
    {
        foreach (uint texture in _textures)
            if (texture != 0)
                gl.DeleteTexture(texture);
        System.Array.Clear(_textures);
        if (_array != 0)
            gl.DeleteTexture(_array);
        if (_framebuffer != 0)
            gl.DeleteFramebuffer(_framebuffer);
        _array = _framebuffer = 0;
    }

    public void Dispose() => Release();
}
