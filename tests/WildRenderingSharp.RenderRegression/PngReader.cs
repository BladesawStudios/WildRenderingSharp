using System.Buffers.Binary;
using System.IO.Compression;

namespace WildRenderingSharp.RenderRegression;

/// <summary>A decoded 8-bit RGBA image, top row first.</summary>
sealed record Image(int Width, int Height, byte[] Rgba);

/// <summary>Reads the 8-bit RGB or RGBA, non-interlaced PNGs the renderer writes, with any of the five scanline filters.</summary>
static class PngReader
{
    static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static Image Read(string path) => Decode(File.ReadAllBytes(path));

    public static Image Decode(byte[] png)
    {
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException("Not a PNG.");

        int width = 0, height = 0, channels = 0;
        using var compressed = new MemoryStream();
        for (int at = 8; at + 8 <= png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            string type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            var data = png.AsSpan(at + 8, length);
            if (type == "IHDR")
                (width, height, channels) = ReadHeader(data);
            else if (type == "IDAT")
                compressed.Write(data);
            else if (type == "IEND")
                break;
            at += 12 + length;
        }

        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        var raw = new byte[(width * channels + 1) * height];
        zlib.ReadExactly(raw);
        return new Image(width, height, Unfilter(raw, width, height, channels));
    }

    static (int Width, int Height, int Channels) ReadHeader(ReadOnlySpan<byte> header)
    {
        int width = BinaryPrimitives.ReadInt32BigEndian(header), height = BinaryPrimitives.ReadInt32BigEndian(header[4..]);
        if (header[8] != 8 || header[12] != 0 || header[9] is not (2 or 6))
            throw new NotSupportedException("Only 8-bit RGB or RGBA PNGs without interlacing are supported.");
        return (width, height, header[9] == 6 ? 4 : 3);
    }

    // Undoes each row's filter and widens RGB to RGBA.
    static byte[] Unfilter(byte[] raw, int width, int height, int channels)
    {
        int stride = width * channels;
        var rows = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            byte filter = raw[y * (stride + 1)];
            for (int x = 0; x < stride; x++)
            {
                int left = x >= channels ? rows[y * stride + x - channels] : 0;
                int up = y > 0 ? rows[(y - 1) * stride + x] : 0;
                int upLeft = x >= channels && y > 0 ? rows[(y - 1) * stride + x - channels] : 0;
                rows[y * stride + x] = (byte)(raw[y * (stride + 1) + 1 + x] + Predict(filter, left, up, upLeft));
            }
        }
        return channels == 4 ? rows : WidenToRgba(rows, width * height);
    }

    static int Predict(byte filter, int left, int up, int upLeft) => filter switch
    {
        0 => 0,
        1 => left,
        2 => up,
        3 => (left + up) / 2,
        4 => Paeth(left, up, upLeft),
        _ => throw new InvalidDataException($"Unknown PNG filter {filter}."),
    };

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    static byte[] WidenToRgba(byte[] rgb, int pixels)
    {
        var rgba = new byte[pixels * 4];
        for (int i = 0; i < pixels; i++)
        {
            rgba[i * 4] = rgb[i * 3];
            rgba[i * 4 + 1] = rgb[i * 3 + 1];
            rgba[i * 4 + 2] = rgb[i * 3 + 2];
            rgba[i * 4 + 3] = 255;
        }
        return rgba;
    }
}
