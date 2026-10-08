using Silk.NET.OpenGL;

namespace WildRenderingSharp.Assets;

public sealed class LoadedTexture
{
    public required uint Handle { get; set; }

    /// <summary>What the handle is bound as - a 2D texture, or a host's texture array (<see cref="ExternalTextures"/>).</summary>
    public TextureTarget Target { get; init; } = TextureTarget.Texture2D;
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>The romfs texture name (e.g. "Cmn_Enemy_DungeonBoss_Eye_Alb"), so a pass can identify a specific asset, as <c>KnownMaterialFixes</c> does.</summary>
    public required string Name { get; init; }
}

/// <summary>
/// One resolved texture binding on a shape: the shader unit, the sampler key it was bound through (e.g. "_a0"), and the
/// texture. The key is carried because a texture pattern anim re-points a sampler by key.
/// </summary>
public readonly record struct ShapeSampler(int Unit, string Key, LoadedTexture Texture);

/// <summary>
/// Loads and caches textures by name so shapes sharing a texture share one GL object. Compressed block data is uploaded
/// straight to the GPU (see <see cref="CompressedTextureFormat"/>); a texture in an unhandled format, or whose bin file is
/// absent, is skipped rather than aborting the model.
/// </summary>
public sealed class TextureCache : IDisposable
{
    readonly GL _gl;
    readonly string _dataDirectory;
    readonly Dictionary<string, LoadedTexture> _byTextureName = new(StringComparer.Ordinal);

    ExternalTextures? _external;
    readonly SharedTextures? _shared;
    bool _ownsExternal;

    /// <param name="external">Where array samplers find a host's textures (see <see cref="ExternalTextures"/>); null binds them white.</param>
    /// <param name="shared">Textures shared with other models' caches (see <see cref="SharedTextures"/>); null keeps them to this one.</param>
    public TextureCache(GL gl, string dataDirectory, ExternalTextures? external = null, SharedTextures? shared = null)
    {
        _gl = gl;
        _dataDirectory = dataDirectory;
        _external = external;
        _shared = shared;
    }

    /// <summary>Resolves every texture a shape's sampler list references, skipping unbound units and logging anything it can't load. Returns bindings ready to bind.</summary>
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

    /// <summary>Loads (or returns the cached) texture for one binding; public so a texture pattern anim can pull in an alternate. Cached by name, so the first binding to ask decides the sRGB interpretation.</summary>
    public LoadedTexture? Load(SamplerBinding s) => GetOrLoad(s);

    static bool? _anisotropy;

    /// <summary>Anisotropic filtering is core only from GL 4.6; before that it is an extension nearly every desktop driver has.</summary>
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

    /// <summary>
    /// Applies the texture's component swizzle. NVN swizzles per texture descriptor, not by the format's channel count, so
    /// GL's identity swizzle can disagree with what a shader expects from a missing channel. The authority is the TXTG
    /// container's <c>CompSelect</c> bytes (<see cref="SamplerBinding.CompSelect"/>), in the Switch-Toolbox encoding
    /// (<c>0=R, 1=G, 2=B, 3=A, 4=Zero, 5=One</c>); see <see cref="MapCompSelect"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This encoding was checked against raw romfs bytes: most textures carry the identity <c>[0,1,2,3]</c>, while unrelated
    /// "Gn4" and "AO" mask textures across character models consistently carry <c>[0,1,1,1]</c>, the standard trick of packing
    /// a second mask into a two-channel BC5's constant B and A. An encoding derived from a general BNTX header broke textures
    /// corpus-wide and was reverted. Falls back to the old BC4-to-RRRR heuristic when a manifest predates the field.
    /// </para>
    /// <para>
    /// Two regressions came from this mechanism. (1) BC4 has one stored channel but can carry <c>[0,1,1,1]</c>; mapping
    /// index 1 to <c>GL_GREEN</c> reads the constant 0 GL defines for a RED-only format's G slot, so reads that should be
    /// grayscale came out red. (2) Clamping every format's index to its channel count broke ordinary BC5 textures: GL's
    /// natural fallback for BC5's missing B and A (0 and 1) already matches what shaders expect, and clamping replaced it
    /// with a duplicated green.
    /// </para>
    /// <para>
    /// The distinction: the game broadcasts a BC4 texture's one channel into all four slots, whereas GL gives (R,0,0,1), so
    /// BC4 alone is overridden. BC5 and four-channel formats use the value literally, trusting GL's per-format channel semantics.
    /// </para>
    /// </remarks>
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

    /// <param name="isBc4">BC4 has one stored channel and the game broadcasts it into every output slot, so any index 0-3 means that channel. Every other format uses the index literally.</param>
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

    /// <summary>
    /// Maps a GX2 wrap mode name (<see cref="SamplerBinding.WrapU"/>, <see cref="SamplerBinding.WrapV"/>) to GL. Null (an
    /// older manifest) keeps repeat. GL has no per-variant equivalent of GX2's border-colour clamps without a border colour,
    /// so all collapse to clamp-to-edge, which gives the "does not tile" behaviour that matters; the MirrorOnce variants
    /// collapse the same way, since they stop reflecting after the first reflection. Plain strings rather than
    /// <c>nameof</c> because this library has no BfresLibrary reference.
    /// </summary>
    static GLEnum MapWrapMode(string? wrap) => wrap switch
    {
        null or "Wrap" => GLEnum.Repeat,
        "Mirror" => GLEnum.MirroredRepeat,
        _ => GLEnum.ClampToEdge, // Clamp, ClampBorder, ClampHalfBorder, ClampToEdge and the MirrorOnce variants
    };

    LoadedTexture? _white;

    /// <summary>A 1x1 opaque white texture, made the first time a binding has nothing else.</summary>
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
