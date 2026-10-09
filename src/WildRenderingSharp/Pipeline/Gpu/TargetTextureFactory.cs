using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline.Gpu;

/// <summary>Creates the cleared textures the render targets are made of, and remembers the viewport-sized ones so a resize can delete them together.</summary>
sealed unsafe class TargetTextureFactory(GL gl)
{
    readonly List<uint> _owned = [];

    // Deletes every texture made since the last release.
    public void ReleaseOwned()
    {
        foreach (uint texture in _owned)
            gl.DeleteTexture(texture);
        _owned.Clear();
    }

    public GpuTexture Color(int width, int height, InternalFormat format, bool repeat = true, bool filterNearest = false)
    {
        uint handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, handle);
        var pixelType = format == InternalFormat.Rgba8 ? PixelType.UnsignedByte : PixelType.Float;
        gl.TexImage2D(TextureTarget.Texture2D, 0, format, (uint)width, (uint)height, 0, PixelFormat.Rgba, pixelType, null);
        // A texture allocated with no data holds whatever the card's memory held, which a pass that is skipped would then show.
        gl.ClearTexImage(handle, 0, PixelFormat.Rgba, pixelType, null);
        gl.SetSampling(TextureTarget.Texture2D, filterNearest ? GLEnum.Nearest : GLEnum.Linear, repeat ? GLEnum.Repeat : GLEnum.ClampToEdge);
        return Own(handle, width, height);
    }

    public GpuTexture ColorArray(int width, int height, InternalFormat format, int layers)
    {
        uint handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2DArray, handle);
        gl.TexImage3D(TextureTarget.Texture2DArray, 0, format, (uint)width, (uint)height, (uint)layers, 0, PixelFormat.Rgba, PixelType.Float, null);
        gl.ClearTexImage(handle, 0, PixelFormat.Rgba, PixelType.Float, null);
        gl.SetSampling(TextureTarget.Texture2DArray, GLEnum.Linear, GLEnum.ClampToEdge);
        return Own(handle, width, height);
    }

    public GpuTexture Depth(int width, int height)
    {
        var texture = DepthTexture(width, height);
        _owned.Add(texture.Handle);
        return texture;
    }

    // A fixed-size depth texture the caller keeps across resizes, compared against by the shadow sampler.
    public GpuTexture ShadowMapDepth(int size)
    {
        var texture = DepthTexture(size, size);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)GLEnum.CompareRefToTexture);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareFunc, (int)GLEnum.Lequal);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        return texture;
    }

    // A fixed-size depth array, one layer per shadow cascade.
    public GpuTexture ShadowCascadeArray(int size, int layers)
    {
        uint handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2DArray, handle);
        gl.TexImage3D(TextureTarget.Texture2DArray, 0, InternalFormat.DepthComponent32f, (uint)size, (uint)size, (uint)layers, 0,
            PixelFormat.DepthComponent, PixelType.Float, null);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareMode, (int)GLEnum.CompareRefToTexture);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareFunc, (int)GLEnum.Lequal);
        gl.SetSampling(TextureTarget.Texture2DArray, GLEnum.Linear, GLEnum.ClampToEdge);
        return new GpuTexture(handle, size, size);
    }

    GpuTexture Own(uint handle, int width, int height)
    {
        _owned.Add(handle);
        return new GpuTexture(handle, width, height);
    }

    // Cleared to the far plane, and nearest-filtered until a caller says otherwise.
    GpuTexture DepthTexture(int width, int height)
    {
        uint handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, handle);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent32f, (uint)width, (uint)height, 0, PixelFormat.DepthComponent, PixelType.Float, null);
        float far = 1f;
        gl.ClearTexImage(handle, 0, PixelFormat.DepthComponent, PixelType.Float, &far);
        gl.SetSampling(TextureTarget.Texture2D, GLEnum.Nearest, GLEnum.ClampToEdge);
        return new GpuTexture(handle, width, height);
    }
}
