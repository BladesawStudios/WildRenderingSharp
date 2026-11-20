using KingRenderingSharp.Gpu;
using KingRenderingSharp.Core.Logging;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Games.Totk.Sky.Clouds;

/// <summary>
/// The three masks the game registers in its cloud texture table, extracted from the romfs by the preparer, and the flat stand-in for the
/// sky bake's scatter table. A missing mask degrades the cloud's shape instead of failing.
/// </summary>
public sealed class CloudMasks : IDisposable
{
    public const int Count = 3;

    readonly GpuApi _gl;
    readonly uint[] _masks = new uint[Count];

    public CloudMasks(GpuApi gl, IContentFiles? systemTextures)
    {
        _gl = gl;
        for (int slot = 0; slot < Count; slot++)
            _masks[slot] = LoadOrFallback(gl, systemTextures, FileName(slot), slot == 0 ? 0.65f : 0.5f);
        ScatterPlaceholder = CreatePlaceholder(gl, 0.05f);
    }

    // Dark, since the scatter value is added to the cloud colour and white would brighten every cloud.
    public uint ScatterPlaceholder { get; }

    public uint this[int slot] => _masks[(uint)slot < Count ? slot : 0];

    public static string FileName(int slot) => $"CloudMask{slot}";

    public void Dispose()
    {
        foreach (uint mask in _masks)
            _gl.DeleteTexture(mask);
        _gl.DeleteTexture(ScatterPlaceholder);
    }

    // An R8 texture swizzled to RRRR as the game's BC4 textures are, with a full mip chain, since the dome's far end would otherwise
    // sample a 512x512 mask far below its resolution and alias into speckle.
    static unsafe uint LoadOrFallback(GpuApi gl, IContentFiles? files, string name, float fallbackValue)
    {
        if (!TryRead(files, name, out byte[] pixels, out int width, out int height))
            return CreatePlaceholder(gl, fallbackValue);

        uint texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        fixed (byte* p = pixels)
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8, (uint)width, (uint)height, 0, PixelFormat.Red, PixelType.UnsignedByte, p);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        foreach (var channel in new[] { TextureParameterName.TextureSwizzleR, TextureParameterName.TextureSwizzleG, TextureParameterName.TextureSwizzleB, TextureParameterName.TextureSwizzleA })
            gl.TexParameter(TextureTarget.Texture2D, channel, (int)GLEnum.Red);
        gl.GenerateMipmap(TextureTarget.Texture2D);
        gl.SetSampling(TextureTarget.Texture2D, GLEnum.Linear, GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        gl.SetAnisotropy(TextureTarget.Texture2D);
        Log.Info($"[CloudDomePass] loaded real {name} mask ({width}x{height}) from the system-texture cache.");
        return texture;
    }

    static bool TryRead(IContentFiles? files, string name, out byte[] pixels, out int width, out int height)
    {
        pixels = [];
        width = height = 0;
        if (SystemTexture.TryRead(files, name) is not { Channels: 1 } texture)
            return false;

        (pixels, width, height) = (texture.Texels, texture.Width, texture.Height);
        return true;
    }

    static unsafe uint CreatePlaceholder(GpuApi gl, float value)
    {
        uint texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        byte v = (byte)(value * 255f);
        byte[] pixel = [v, v, v, 255];
        fixed (byte* p = pixel)
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        gl.SetSampling(TextureTarget.Texture2D, GLEnum.Linear, GLEnum.Repeat);
        return texture;
    }
}
