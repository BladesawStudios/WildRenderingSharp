namespace KingRenderingSharp.Core.Storage;

/// <summary>A plain texture of the system textures store: one file of its width, height, depth and channel count as four 32-bit integers, then its texels.</summary>
public sealed record SystemTexture(int Width, int Height, int Depth, int Channels, byte[] Texels)
{
    public static string FileOf(string name) => name + ".tex";

    public byte[] ToBytes()
    {
        var bytes = new byte[16 + Texels.Length];
        BitConverter.TryWriteBytes(bytes.AsSpan(0), Width);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), Height);
        BitConverter.TryWriteBytes(bytes.AsSpan(8), Depth);
        BitConverter.TryWriteBytes(bytes.AsSpan(12), Channels);
        Texels.CopyTo(bytes, 16);
        return bytes;
    }

    /// <summary>The texture of that name, or null when the store has none or its texels are shorter than its size says.</summary>
    public static SystemTexture? TryRead(IContentFiles? files, string name)
    {
        if (files is null || !files.Exists(FileOf(name))) return null;
        byte[] bytes = files.ReadBytes(FileOf(name));
        if (bytes.Length < 16) return null;
        int width = BitConverter.ToInt32(bytes, 0), height = BitConverter.ToInt32(bytes, 4), depth = BitConverter.ToInt32(bytes, 8), channels = BitConverter.ToInt32(bytes, 12);
        long size = (long)width * height * depth * channels;
        return width > 0 && height > 0 && depth > 0 && channels > 0 && bytes.Length - 16 >= size ? new SystemTexture(width, height, depth, channels, bytes[16..]) : null;
    }
}
