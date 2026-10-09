using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Manifests;

namespace WildRenderingSharp.Assets.Textures;

/// <summary>Loads and caches textures by name so shapes sharing a texture share one GL object.</summary>
internal sealed class TextureCache : IDisposable
{
    readonly GL _gl;
    readonly string _dataDirectory;
    readonly SharedTextures? _shared;
    readonly TextureUploader _uploader;
    readonly Dictionary<string, LoadedTexture> _byTextureName = new(StringComparer.Ordinal);

    ExternalTextures? _external;
    bool _ownsExternal;
    LoadedTexture? _white;

    public TextureCache(GL gl, string dataDirectory, ExternalTextures? external = null, SharedTextures? shared = null)
    {
        _gl = gl;
        _dataDirectory = dataDirectory;
        _external = external;
        _shared = shared;
        _uploader = new TextureUploader(gl);
    }

    public List<ShapeSampler> Resolve(IEnumerable<SamplerBinding> samplers)
    {
        var result = new List<ShapeSampler>();
        foreach (var s in samplers)
        {
            // Bound to white when the texture is missing: an unbound unit reads whatever the last draw left on it, which once turned every static world object black.
            var texture = ExternalTextures.IsArraySampler(s.Key) ? ExternalTexture(s)
                : string.IsNullOrEmpty(s.File) ? null : GetOrLoad(s);
            result.Add(new ShapeSampler(s.Unit, s.Key, texture ?? White()));
        }
        return result;
    }

    public LoadedTexture? Load(SamplerBinding s) => GetOrLoad(s);

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

    // Never the model's own one-layer copy (see ExternalTextures).
    LoadedTexture ExternalTexture(SamplerBinding s)
    {
        if (_external is null)
        {
            _external = new ExternalTextures(_gl);
            _ownsExternal = true;
        }
        return _external.Get(s.Texture);
    }

    LoadedTexture? GetOrLoad(SamplerBinding s)
    {
        if (_byTextureName.TryGetValue(s.Texture, out var cached))
            return cached;

        if (CompressedTextureFormat.Resolve(s.Format) is not { } info)
        {
            Console.WriteLine($"[TextureCache] SKIPPED '{s.Texture}': unhandled format '{s.Format}'");
            return null;
        }

        string? sharedKey = _shared is null ? null
            : SharedTextures.Key(s, CompressedTextureFormat.IsSrgb(s.Format, s.Key, s.Assigned, s.Texture));
        if (sharedKey is not null && _shared!.TryAcquire(sharedKey, out var held))
            return _byTextureName[s.Texture] = held;

        string path = Path.Combine(_dataDirectory, s.File);
        if (!File.Exists(path))
        {
            Console.WriteLine($"[TextureCache] SKIPPED '{s.Texture}': missing file '{s.File}'");
            return null;
        }

        var loaded = _uploader.Upload(s, info, File.ReadAllBytes(path));
        _byTextureName[s.Texture] = loaded;
        if (sharedKey is not null)
            _shared!.Add(sharedKey, loaded);
        return loaded;
    }

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
}
