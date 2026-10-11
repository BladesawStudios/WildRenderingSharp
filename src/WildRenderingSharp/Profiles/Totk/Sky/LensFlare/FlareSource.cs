using Silk.NET.OpenGL;

namespace WildRenderingSharp.Profiles.Totk.Sky.LensFlare;

/// <summary>A ping-pong pair of small HDR targets the flare's bright pass is blurred in; slot 0 is where each blur ends and what the flare samples.</summary>
sealed class FlareSource(GL gl) : IDisposable
{
    readonly uint[] _textures = new uint[2], _framebuffers = new uint[2];

    public int Width { get; private set; }

    public int Height { get; private set; }

    public uint Texture(int slot) => _textures[slot];

    public void BindTarget(int slot) => gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffers[slot]);

    // Recreates both targets when the size changes.
    public void Ensure(int width, int height)
    {
        if (width == Width && height == Height && _textures[0] != 0)
            return;
        (Width, Height) = (width, height);
        for (int i = 0; i < 2; i++)
        {
            if (_textures[i] != 0)
                gl.DeleteTexture(_textures[i]);
            if (_framebuffers[i] == 0)
                _framebuffers[i] = gl.GenFramebuffer();
            _textures[i] = CreateTexture(width, height);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffers[i]);
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _textures[i], 0);
        }
    }

    public void Dispose()
    {
        for (int i = 0; i < 2; i++)
        {
            if (_textures[i] != 0)
                gl.DeleteTexture(_textures[i]);
            if (_framebuffers[i] != 0)
                gl.DeleteFramebuffer(_framebuffers[i]);
        }
    }

    // Clamped to a black border, not to edge: the ghost taps walk past the far side of the screen, and clamp-to-edge would smear the border pixel into a ghost.
    unsafe uint CreateTexture(int width, int height)
    {
        uint texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.Float, null);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToBorder);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToBorder);
        Span<float> border = [0f, 0f, 0f, 0f];
        fixed (float* b = border)
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, b);
        return texture;
    }
}
