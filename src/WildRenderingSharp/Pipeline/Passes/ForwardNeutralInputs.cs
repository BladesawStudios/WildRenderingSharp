using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;

namespace WildRenderingSharp.Pipeline.Passes;

/// <summary>The textures the forward programs sample that the frame has no real source for: neutral values, plus the game's noise volume when it was extracted.</summary>
internal sealed class ForwardNeutralInputs : IDisposable
{
    static readonly int[] WhiteUnits = [7, 11, 13, 14, 15, 31];

    readonly GL _gl;
    readonly uint _white, _volumeMask, _noise3D, _arrayWhite, _shadowCascades;

    public ForwardNeutralInputs(GL gl, string? systemTexturesDirectory)
    {
        _gl = gl;
        _white = ConstantTextures.Rgba(gl, 1, 1, 1, 1);
        _volumeMask = ConstantTextures.Rgba(gl, 0, 0, 0, 0);
        _arrayWhite = ConstantTextures.RgbaArray(gl, 1, 1, 1, 1);
        _noise3D = NoiseVolume(systemTexturesDirectory);
        _shadowCascades = NoShadowCascades();
    }

    public void Bind()
    {
        foreach (int unit in WhiteUnits)
            _gl.BindTextureAt(unit, _white);
        _gl.BindTextureAt(10, _volumeMask);
        _gl.BindTextureAt(9, _noise3D, TextureTarget.Texture3D);
        _gl.BindTextureAt(29, _arrayWhite, TextureTarget.Texture2DArray);
        _gl.BindTextureAt(6, _shadowCascades, TextureTarget.Texture2DArray);
    }

    public void Dispose()
    {
        foreach (uint texture in new[] { _white, _volumeMask, _noise3D, _arrayWhite, _shadowCascades })
            _gl.DeleteTexture(texture);
    }

    // A 1x1 depth array whose comparison always passes, so nothing occludes: an unbound sampler of the wrong type reads as undefined and made NaN across the frame.
    unsafe uint NoShadowCascades()
    {
        uint handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2DArray, handle);
        float depthOne = 1.0f;
        _gl.TexImage3D(TextureTarget.Texture2DArray, 0, InternalFormat.DepthComponent32f, 1, 1, 1, 0, PixelFormat.DepthComponent, PixelType.Float, &depthOne);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareMode, (int)GLEnum.CompareRefToTexture);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareFunc, (int)GLEnum.Lequal);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        return handle;
    }

    // cTex_Proc3DNoise: the extracted Worley and Perlin volume, else a flat mid-grey that makes every branch sampling it uniform.
    uint NoiseVolume(string? systemTexturesDirectory)
    {
        if (systemTexturesDirectory is { } dir)
        {
            string data = Path.Combine(dir, "Proc3DNoise.r8"), dims = Path.Combine(dir, "Proc3DNoise.dims.txt");
            if (File.Exists(data) && File.Exists(dims) && TryUploadNoise(data, dims) is { } handle)
                return handle;
        }
        return FallbackNoise();
    }

    unsafe uint? TryUploadNoise(string dataPath, string dimsPath)
    {
        var parts = File.ReadAllText(dimsPath).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int w = int.Parse(parts[0]), h = int.Parse(parts[1]), d = int.Parse(parts[2]);
        byte[] raw = File.ReadAllBytes(dataPath);
        if (raw.Length != w * h * d)
            return null;

        uint handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture3D, handle);
        fixed (byte* ptr = raw)
            _gl.TexImage3D(TextureTarget.Texture3D, 0, InternalFormat.R8, (uint)w, (uint)h, (uint)d, 0, PixelFormat.Red, PixelType.UnsignedByte, ptr);
        // Broadcast like every other single-channel texture.
        _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureSwizzleG, (int)GLEnum.Red);
        _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureSwizzleB, (int)GLEnum.Red);
        _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureSwizzleA, (int)GLEnum.Red);
        _gl.SetSampling(TextureTarget.Texture3D, GLEnum.Linear, GLEnum.Repeat);
        return handle;
    }

    unsafe uint FallbackNoise()
    {
        uint handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture3D, handle);
        Span<float> grey = [0.5f, 0.5f, 0.5f, 1f];
        fixed (float* ptr = grey)
            _gl.TexImage3D(TextureTarget.Texture3D, 0, InternalFormat.Rgba32f, 1, 1, 1, 0, PixelFormat.Rgba, PixelType.Float, ptr);
        _gl.SetSampling(TextureTarget.Texture3D, GLEnum.Linear, GLEnum.Repeat);
        return handle;
    }
}
