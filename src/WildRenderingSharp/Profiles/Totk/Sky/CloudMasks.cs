using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>
/// The three masks the game registers in its cloud texture table, extracted from the romfs by the preparer, and the flat stand-in for the
/// sky bake's scatter table. A missing mask degrades the cloud's shape instead of failing.
/// </summary>
public sealed class CloudMasks : IDisposable
{
    public const int Count = 3;

    readonly GL _gl;
    readonly uint[] _masks = new uint[Count];

    public CloudMasks(GL gl, string? systemTexturesDirectory)
    {
        _gl = gl;
        for (int slot = 0; slot < Count; slot++)
            _masks[slot] = LoadOrFallback(gl, systemTexturesDirectory, FileName(slot), slot == 0 ? 0.65f : 0.5f);
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
    static unsafe uint LoadOrFallback(GL gl, string? directory, string name, float fallbackValue)
    {
        if (!TryRead(directory, name, out byte[] pixels, out int width, out int height))
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
        gl.TexParameter(TextureTarget.Texture2D, (TextureParameterName)GLEnum.TextureMaxAnisotropy, 8f);
        Console.WriteLine($"[CloudDomePass] loaded real {name} mask ({width}x{height}) from the system-texture cache.");
        return texture;
    }

    static bool TryRead(string? directory, string name, out byte[] pixels, out int width, out int height)
    {
        pixels = [];
        width = height = 0;
        if (string.IsNullOrEmpty(directory))
            return false;

        string dataPath = Path.Combine(directory, name + ".r8");
        string dimsPath = Path.Combine(directory, name + ".dims.txt");
        if (!File.Exists(dataPath) || !File.Exists(dimsPath))
            return false;

        string[] dims = File.ReadAllText(dimsPath).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (dims.Length < 2 || !int.TryParse(dims[0], out width) || !int.TryParse(dims[1], out height))
            return false;

        pixels = File.ReadAllBytes(dataPath);
        return pixels.Length >= width * height;
    }

    static unsafe uint CreatePlaceholder(GL gl, float value)
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
