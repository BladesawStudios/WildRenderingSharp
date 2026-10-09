using Silk.NET.OpenGL;

namespace WildRenderingSharp.Assets.Textures;

/// <summary>Textures a host supplies by name instead of each model carrying its own - the terrain's material arrays above all.</summary>
public sealed class ExternalTextures : IDisposable
{
    readonly GL _gl;
    readonly Dictionary<string, LoadedTexture> _byName = new(StringComparer.Ordinal);
    uint _whiteArray;

    public ExternalTextures(GL gl) => _gl = gl;

    public static bool IsArraySampler(string key) => key.StartsWith("array", StringComparison.Ordinal);

    public LoadedTexture Get(string name)
    {
        lock (_byName)
            return GetLocked(name);
    }

    LoadedTexture GetLocked(string name)
    {
        if (!_byName.TryGetValue(name, out var texture))
        {
            texture = new LoadedTexture { Handle = WhiteArray(), Width = 1, Height = 1, Name = name, Target = TextureTarget.Texture2DArray };
            _byName[name] = texture;
        }
        return texture;
    }

    public void Set(string name, uint handle)
    {
        lock (_byName)
        {
            var texture = GetLocked(name);
            texture.Handle = handle != 0 ? handle : WhiteArray();
        }
    }

    public void Clear(string name) => Set(name, 0);

    unsafe uint WhiteArray()
    {
        if (_whiteArray != 0)
            return _whiteArray;
        _whiteArray = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2DArray, _whiteArray);
        byte* texel = stackalloc byte[] { 255, 255, 255, 255 };
        _gl.TexImage3D(TextureTarget.Texture2DArray, 0, InternalFormat.Rgba8, 1, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, texel);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        _gl.BindTexture(TextureTarget.Texture2DArray, 0);
        return _whiteArray;
    }

    public void Dispose()
    {
        if (_whiteArray != 0)
            _gl.DeleteTexture(_whiteArray);
        _whiteArray = 0;
        _byName.Clear();
    }
}
