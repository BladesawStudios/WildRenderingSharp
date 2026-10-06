using Silk.NET.OpenGL;

namespace WildRenderingSharp.Assets;

/// <summary>
/// Textures a host supplies by name instead of each model carrying its own - the terrain's
/// material arrays above all.
/// </summary>
/// <remarks>
/// <para>
/// Objects that blend into the ground (<c>_Bld</c>, <c>_Seal</c>, grass fields) sample the
/// terrain's own 121-layer material arrays, <c>MaterialAlb</c> and <c>MaterialCmb</c>, through
/// <c>sampler2DArray</c>s their materials name <c>array0</c>/<c>array1</c>. A model's own export
/// holds only one layer of them as a 2D texture, and a 2D texture on an array sampler reads
/// nothing, so those objects came out black. Decoding the whole arrays per model would be hundreds
/// of megabytes each; a host that already has them on the GPU - a map viewer drawing the terrain -
/// hands its own over with <see cref="Set"/>.
/// </para>
/// <para>
/// Until it does, each name binds a 1x1 white array: neutral, never black. Every shape keeps the
/// same <see cref="LoadedTexture"/> object for a name, and <see cref="Set"/> repoints it, so
/// models loaded before the host's arrays were ready pick them up without reloading.
/// </para>
/// </remarks>
public sealed class ExternalTextures : IDisposable
{
    readonly GL _gl;
    readonly Dictionary<string, LoadedTexture> _byName = new(StringComparer.Ordinal);
    uint _whiteArray;

    public ExternalTextures(GL gl) => _gl = gl;

    /// <summary>Whether a material sampler, by its shading-model key, reads a texture array - one a host supplies rather than the model.</summary>
    public static bool IsArraySampler(string key) => key.StartsWith("array", StringComparison.Ordinal);

    /// <summary>The texture bound for <paramref name="name"/> - the host's, once set, otherwise white.</summary>
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

    /// <summary>
    /// Supplies <paramref name="name"/> as a host's texture array. The host keeps ownership and
    /// must call this again (or <see cref="Clear"/>) before deleting it.
    /// </summary>
    public void Set(string name, uint handle)
    {
        lock (_byName)
        {
            var texture = GetLocked(name);
            texture.Handle = handle != 0 ? handle : WhiteArray();
        }
    }

    /// <summary>Puts <paramref name="name"/> back to white - the host is about to delete its texture.</summary>
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
