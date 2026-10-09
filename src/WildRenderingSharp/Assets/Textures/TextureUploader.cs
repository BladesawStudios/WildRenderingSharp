using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Manifests;
using WildRenderingSharp.Gpu;

namespace WildRenderingSharp.Assets.Textures;

/// <summary>Uploads one exported texture, its mip chain and its sampling state, to a new GL texture.</summary>
sealed class TextureUploader(GL gl)
{
    const float MaxAnisotropy = 8f;

    bool? _anisotropy;

    public LoadedTexture Upload(SamplerBinding s, CompressedTextureFormat.Info info, byte[] raw)
    {
        bool srgb = CompressedTextureFormat.IsSrgb(s.Format, s.Key, s.Assigned, s.Texture);

        // Start from an empty error queue so the check below is about this upload: alternates load on demand mid-frame, after the pipeline ran.
        GLDiagnostics.CheckPending(gl, $"uploading texture '{s.Texture}'");
        uint handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, handle);
        int levels = UploadLevels(s, info, raw, srgb);
        GLDiagnostics.Check(gl, $"uploading texture '{s.Texture}' ({s.Format}, {s.Width}x{s.Height}, {raw.Length} bytes)");

        ApplySwizzle(s);
        ApplySampling(s, levels);
        return new LoadedTexture { Handle = handle, Width = s.Width, Height = s.Height, Name = s.Texture };
    }

    // The file is the mip chain back to back, or mip 0 alone from an older cache; levels are read for as long as another whole one remains.
    int UploadLevels(SamplerBinding s, CompressedTextureFormat.Info info, byte[] raw, bool srgb)
    {
        var internalFormat = srgb ? info.FormatSrgb : info.Format;
        int levels = 0;
        for (int offset = 0, w = s.Width, h = s.Height; ; w = Math.Max(1, w / 2), h = Math.Max(1, h / 2))
        {
            int length = CompressedTextureFormat.ComputeDataLength(info, w, h);
            if (offset + length > raw.Length)
                break;
            UploadLevel(levels, internalFormat, new ReadOnlySpan<byte>(raw, offset, length), w, h, info, srgb);
            levels++;
            offset += length;
            if (w == 1 && h == 1)
                break;
        }
        return levels;
    }

    unsafe void UploadLevel(int level, InternalFormat format, ReadOnlySpan<byte> data, int w, int h, CompressedTextureFormat.Info info, bool srgb)
    {
        if (info.AstcFootprint is not { } footprint)
        {
            gl.CompressedTexImage2D(TextureTarget.Texture2D, level, format, (uint)w, (uint)h, 0, data);
            return;
        }
        // ASTC has no guaranteed desktop support: decode to RGBA on the CPU rather than risk a silent upload failure.
        byte[] rgba = CompressedTextureFormat.DecodeAstc(data.ToArray(), w, h, footprint, srgb);
        fixed (byte* ptr = rgba)
            gl.TexImage2D(TextureTarget.Texture2D, level, format, (uint)w, (uint)h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
    }

    // Exactly the levels uploaded (an exporter may cut a chain short). Without the chain a tiled texture shimmers at distance;
    // glGenerateMipmap is undefined for compressed formats.
    void ApplySampling(SamplerBinding s, int levels)
    {
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 0);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, Math.Max(0, levels - 1));
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)(levels > 1 ? GLEnum.LinearMipmapLinear : GLEnum.Linear));
        // Surfaces seen edge-on blur under trilinear alone.
        if (levels > 1 && SupportsAnisotropy())
            gl.TexParameter(TextureTarget.Texture2D, (TextureParameterName)GLEnum.TextureMaxAnisotropy, MaxAnisotropy);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)MapWrapMode(s.WrapU));
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)MapWrapMode(s.WrapV));
    }

    bool SupportsAnisotropy() =>
        _anisotropy ??= gl.IsExtensionPresent("GL_EXT_texture_filter_anisotropic") || gl.IsExtensionPresent("GL_ARB_texture_filter_anisotropic");

    // NVN swizzles per texture descriptor, not by the format's channel count, so GL's identity swizzle can disagree with what a shader
    // expects from a missing channel. The authority is the TXTG container's CompSelect bytes, in the Switch-Toolbox encoding
    // (0=R, 1=G, 2=B, 3=A, 4=Zero, 5=One).
    void ApplySwizzle(SamplerBinding s)
    {
        bool isBc4 = s.Format.StartsWith("BC4", StringComparison.Ordinal);
        if (s.CompSelect is { Length: 4 } cs)
        {
            SetSwizzle(MapCompSelect(cs[0], isBc4), MapCompSelect(cs[1], isBc4), MapCompSelect(cs[2], isBc4), MapCompSelect(cs[3], isBc4));
            return;
        }
        if (isBc4)
            SetSwizzle(GLEnum.Red, GLEnum.Red, GLEnum.Red, GLEnum.Red);
    }

    void SetSwizzle(GLEnum r, GLEnum g, GLEnum b, GLEnum a)
    {
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleR, (int)r);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleG, (int)g);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleB, (int)b);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleA, (int)a);
    }

    static GLEnum MapCompSelect(int v, bool isBc4) => v switch
    {
        4 => GLEnum.Zero,
        5 => GLEnum.One,
        >= 0 and <= 3 => isBc4 ? GLEnum.Red : MapRealChannel(v),
        _ => GLEnum.Red, // not observed in real data; defaults to the texture's first channel instead of injecting a constant
    };

    static GLEnum MapRealChannel(int v) => v switch
    {
        0 => GLEnum.Red,
        1 => GLEnum.Green,
        2 => GLEnum.Blue,
        _ => GLEnum.Alpha,
    };

    // Maps a GX2 wrap mode name to GL. Null (an older manifest) keeps repeat. GL has no per-variant equivalent of GX2's border-colour
    // clamps without a border colour, so all collapse to clamp-to-edge, as do the MirrorOnce variants, which stop reflecting after the first.
    static GLEnum MapWrapMode(string? wrap) => wrap switch
    {
        null or "Wrap" => GLEnum.Repeat,
        "Mirror" => GLEnum.MirroredRepeat,
        _ => GLEnum.ClampToEdge,
    };
}
