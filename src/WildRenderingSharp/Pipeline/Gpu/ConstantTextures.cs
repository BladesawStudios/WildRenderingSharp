using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline.Gpu;

/// <summary>Single-texel textures that stand in for inputs a shader reads but the frame has nothing for.</summary>
public static class ConstantTextures
{
    public static unsafe uint Rgba(GL gl, float r, float g, float b, float a)
    {
        uint handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, handle);
        Span<float> texel = [r, g, b, a];
        fixed (float* ptr = texel)
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba32f, 1, 1, 0, PixelFormat.Rgba, PixelType.Float, ptr);
        gl.SetSampling(TextureTarget.Texture2D, GLEnum.Linear, GLEnum.Repeat);
        return handle;
    }

    public static unsafe uint RgbaArray(GL gl, float r, float g, float b, float a)
    {
        uint handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2DArray, handle);
        Span<float> texel = [r, g, b, a];
        fixed (float* ptr = texel)
            gl.TexImage3D(TextureTarget.Texture2DArray, 0, InternalFormat.Rgba32f, 1, 1, 1, 0, PixelFormat.Rgba, PixelType.Float, ptr);
        gl.SetSampling(TextureTarget.Texture2DArray, GLEnum.Linear, GLEnum.Repeat);
        return handle;
    }
}
