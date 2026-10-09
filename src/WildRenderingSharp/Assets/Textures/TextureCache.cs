using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Manifests;
using WildRenderingSharp.Pipeline.Gpu;

namespace WildRenderingSharp.Assets.Textures;

/// <summary>Loads and caches textures by name so shapes sharing a texture share one GL object.</summary>
public sealed class TextureCache : IDisposable
{
    readonly GL _gl;
    readonly string _dataDirectory;
    readonly Dictionary<string, LoadedTexture> _byTextureName = new(StringComparer.Ordinal);

    ExternalTextures? _external;
    readonly SharedTextures? _shared;
    bool _ownsExternal;

    public TextureCache(GL gl, string dataDirectory, ExternalTextures? external = null, SharedTextures? shared = null)
    {
        _gl = gl;
        _dataDirectory = dataDirectory;
        _external = external;
        _shared = shared;
    }

    public List<ShapeSampler> Resolve(IEnumerable<SamplerBinding> samplers)
    {
        var result = new List<ShapeSampler>();
        foreach (var s in samplers)
        {
            // Bound to white when the texture is missing: an unbound unit reads whatever the last draw left on it, which once turned every static world object black.
            if (ExternalTextures.IsArraySampler(s.Key))
            {
                // Never the model's own one-layer copy (see ExternalTextures).
                if (_external is null)
                {
                    _external = new ExternalTextures(_gl);
                    _ownsExternal = true;
                }
                result.Add(new ShapeSampler(s.Unit, s.Key, _external.Get(s.Texture)));
                continue;
            }
            var tex = string.IsNullOrEmpty(s.File) ? null : GetOrLoad(s);
            result.Add(new ShapeSampler(s.Unit, s.Key, tex ?? White()));
        }
        return result;
    }

    public LoadedTexture? Load(SamplerBinding s) => GetOrLoad(s);

    static bool? _anisotropy;

    static bool SupportsAnisotropy(GL gl) =>
        _anisotropy ??= gl.IsExtensionPresent("GL_EXT_texture_filter_anisotropic") || gl.IsExtensionPresent("GL_ARB_texture_filter_anisotropic");

    LoadedTexture? GetOrLoad(SamplerBinding s)
    {
        if (_byTextureName.TryGetValue(s.Texture, out var cached))
            return cached;

        var info = CompressedTextureFormat.Resolve(s.Format);
        if (info is null)
        {
            Console.WriteLine($"[TextureCache] SKIPPED '{s.Texture}': unhandled format '{s.Format}'");
            return null;
        }

        string? sharedKey = _shared is null ? null
            : SharedTextures.Key(s, CompressedTextureFormat.IsSrgb(s.Format, s.Key, s.Assigned, s.Texture));
        if (sharedKey is not null && _shared!.TryAcquire(sharedKey, out var held))
        {
            _byTextureName[s.Texture] = held;
            return held;
        }

        string path = Path.Combine(_dataDirectory, s.File);
        if (!File.Exists(path))
        {
            Console.WriteLine($"[TextureCache] SKIPPED '{s.Texture}': missing file '{s.File}'");
            return null;
        }

        byte[] raw = File.ReadAllBytes(path);
        bool srgb = CompressedTextureFormat.IsSrgb(s.Format, s.Key, s.Assigned, s.Texture);

        // Start from an empty error queue so the check below is about this upload: alternates load on demand mid-frame, after the pipeline ran.
        GLDiagnostics.CheckPending(_gl, $"uploading texture '{s.Texture}'");
        var internalFormat = srgb ? info.Value.FormatSrgb : info.Value.Format;

        uint handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, handle);

        // The file is the mip chain back to back, or mip 0 alone from an older cache; levels are read for as long as another whole one remains.
        int levels = 0;
        for (int offset = 0, w = s.Width, h = s.Height; ; w = Math.Max(1, w / 2), h = Math.Max(1, h / 2))
        {
            int length = CompressedTextureFormat.ComputeDataLength(info.Value, w, h);
            if (offset + length > raw.Length)
                break;
            var level = new ReadOnlySpan<byte>(raw, offset, length);
            if (info.Value.AstcFootprint is { } footprint)
            {
                // ASTC has no guaranteed desktop support: decode to RGBA on the CPU rather than risk a silent upload failure.
                byte[] rgba = CompressedTextureFormat.DecodeAstc(level.ToArray(), w, h, footprint, srgb);
                unsafe
                {
                    fixed (byte* ptr = rgba)
                        _gl.TexImage2D(TextureTarget.Texture2D, levels, internalFormat, (uint)w, (uint)h, 0,
                            PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
                }
            }
            else
            {
                _gl.CompressedTexImage2D(TextureTarget.Texture2D, levels, internalFormat, (uint)w, (uint)h, 0, level);
            }
            levels++;
            offset += length;
            if (w == 1 && h == 1)
                break;
        }

        GLDiagnostics.Check(_gl, $"uploading texture '{s.Texture}' ({s.Format}, {s.Width}x{s.Height}, {raw.Length} bytes)");

        ApplySwizzle(s);

        // Exactly the levels uploaded (an exporter may cut a chain short). Without the chain a tiled texture shimmers at
        // distance; glGenerateMipmap is undefined for compressed formats.
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 0);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, Math.Max(0, levels - 1));
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
            (int)(levels > 1 ? GLEnum.LinearMipmapLinear : GLEnum.Linear));
        // Surfaces seen edge-on blur under trilinear alone.
        if (levels > 1 && SupportsAnisotropy(_gl))
            _gl.TexParameter(TextureTarget.Texture2D, (TextureParameterName)GLEnum.TextureMaxAnisotropy, 8f);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)MapWrapMode(s.WrapU));
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)MapWrapMode(s.WrapV));

        var loaded = new LoadedTexture { Handle = handle, Width = s.Width, Height = s.Height, Name = s.Texture };
        _byTextureName[s.Texture] = loaded;
        if (sharedKey is not null)
            _shared!.Add(sharedKey, loaded);
        return loaded;
    }

    // Applies the texture's component swizzle. NVN swizzles per texture descriptor, not by the format's channel count, so GL's
    // identity swizzle can disagree with what a shader expects from a missing channel. The authority is the TXTG container's
    // CompSelect bytes (CompSelect), in the Switch-Toolbox encoding (0=R, 1=G, 2=B, 3=A, 4=Zero, 5=One); see MapCompSelect.
    void ApplySwizzle(SamplerBinding s)
    {
        bool isBc4 = s.Format.StartsWith("BC4", StringComparison.Ordinal);

        if (s.CompSelect is { Length: 4 } cs)
        {
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleR, (int)MapCompSelect(cs[0], isBc4));
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleG, (int)MapCompSelect(cs[1], isBc4));
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleB, (int)MapCompSelect(cs[2], isBc4));
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleA, (int)MapCompSelect(cs[3], isBc4));
            return;
        }

        if (isBc4)
        {
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleR, (int)GLEnum.Red);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleG, (int)GLEnum.Red);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleB, (int)GLEnum.Red);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleA, (int)GLEnum.Red);
        }
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

    // Maps a GX2 wrap mode name (WrapU, WrapV) to GL. Null (an older manifest) keeps repeat. GL has no per-variant equivalent
    // of GX2's border-colour clamps without a border colour, so all collapse to clamp-to-edge, which gives the "does not tile"
    // behaviour that matters; the MirrorOnce variants collapse the same way, since they stop reflecting after the first
    // reflection.
    static GLEnum MapWrapMode(string? wrap) => wrap switch
    {
        null or "Wrap" => GLEnum.Repeat,
        "Mirror" => GLEnum.MirroredRepeat,
        _ => GLEnum.ClampToEdge, // Clamp, ClampBorder, ClampHalfBorder, ClampToEdge and the MirrorOnce variants
    };

    LoadedTexture? _white;

    unsafe LoadedTexture White()
    {
        if (_white is { } white)
            return white;
        uint handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, handle);
        byte* texel = stackalloc byte[] { 255, 255, 255, 255 };
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, texel);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        return _white = new LoadedTexture { Handle = handle, Width = 1, Height = 1, Name = "(missing)" };
    }

    public void Dispose()
    {
        foreach (var tex in _byTextureName.Values)
        {
            if (_shared is not null && _shared.Owns(tex))
                _shared.Release(tex);
            else
                _gl.DeleteTexture(tex.Handle);
        }
        _byTextureName.Clear();
        if (_white is { } white)
            _gl.DeleteTexture(white.Handle);
        _white = null;
        if (_ownsExternal)
            _external?.Dispose();
    }
}
