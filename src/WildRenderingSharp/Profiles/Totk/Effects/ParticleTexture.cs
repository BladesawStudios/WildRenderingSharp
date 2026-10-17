using Silk.NET.OpenGL;

namespace WildRenderingSharp.Profiles.Totk.Effects;

/// <summary>An emitter's texture read back from the cache, where preparation decoded it to its size and RGBA8 texels.</summary>
internal static class ParticleTexture
{
    // The texture as a mipmapped, repeating GL texture, or 0 when the emitter has none.
    public static unsafe uint Load(GL gl, string path)
    {
        if (!File.Exists(path))
            return 0;

        byte[] file = File.ReadAllBytes(path);
        int width = BitConverter.ToInt32(file, 0), height = BitConverter.ToInt32(file, sizeof(int));
        uint handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, handle);
        fixed (byte* texels = &file[2 * sizeof(int)])
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, texels);
        gl.GenerateMipmap(TextureTarget.Texture2D);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        return handle;
    }
}
