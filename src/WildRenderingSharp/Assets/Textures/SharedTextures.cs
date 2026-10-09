using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Manifests;

namespace WildRenderingSharp.Assets.Textures;

/// <summary>Textures shared between models, counted by how many models hold each.</summary>
public sealed class SharedTextures : IDisposable
{
    readonly GL _gl;
    readonly Dictionary<string, (LoadedTexture Texture, int Refs)> _byKey = new(StringComparer.Ordinal);
    readonly Dictionary<LoadedTexture, string> _keyOf = new(ReferenceEqualityComparer.Instance);

    public SharedTextures(GL gl) => _gl = gl;

    public int Count => _byKey.Count;

    internal static string Key(SamplerBinding s, bool srgb) =>
        $"{s.File}|{srgb}|{(s.CompSelect is { } c ? string.Join(',', c) : "-")}|{s.WrapU}|{s.WrapV}";

    internal bool TryAcquire(string key, out LoadedTexture texture)
    {
        lock (_byKey)
        {
            if (_byKey.TryGetValue(key, out var entry))
            {
                _byKey[key] = (entry.Texture, entry.Refs + 1);
                texture = entry.Texture;
                return true;
            }
            texture = null!;
            return false;
        }
    }

    internal void Add(string key, LoadedTexture texture)
    {
        lock (_byKey)
        {
            _byKey[key] = (texture, 1);
            _keyOf[texture] = key;
        }
    }

    internal bool Owns(LoadedTexture texture)
    {
        lock (_byKey)
            return _keyOf.ContainsKey(texture);
    }

    internal void Release(LoadedTexture texture)
    {
        lock (_byKey)
            ReleaseLocked(texture);
    }

    void ReleaseLocked(LoadedTexture texture)
    {
        if (!_keyOf.TryGetValue(texture, out string? key) || !_byKey.TryGetValue(key, out var entry))
            return;
        if (entry.Refs > 1)
        {
            _byKey[key] = (entry.Texture, entry.Refs - 1);
            return;
        }
        _byKey.Remove(key);
        _keyOf.Remove(texture);
        _gl.DeleteTexture(texture.Handle);
    }

    public void Dispose()
    {
        foreach (var (texture, _) in _byKey.Values)
            _gl.DeleteTexture(texture.Handle);
        _byKey.Clear();
        _keyOf.Clear();
    }
}
