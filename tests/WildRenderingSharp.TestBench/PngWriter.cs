using System.IO.Compression;

namespace WildRenderingSharp.TestBench;

static class PngWriter
{
    public static void Write(string path, byte[] rgba, int width, int height)
    {
        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> header = stackalloc byte[13];
        BitConverter.TryWriteBytes(header, System.Net.IPAddress.HostToNetworkOrder(width));
        BitConverter.TryWriteBytes(header[4..], System.Net.IPAddress.HostToNetworkOrder(height));
        header[8] = 8;
        header[9] = 6;
        Chunk(file, "IHDR", header);

        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                z.WriteByte(0);
                z.Write(rgba, y * width * 4, width * 4);
            }
        }
        Chunk(file, "IDAT", raw.ToArray());
        Chunk(file, "IEND", []);
    }

    static void Chunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        s.Write(word);
        var body = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, body);
        data.CopyTo(body.AsSpan(4));
        s.Write(body);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(word, Crc(body));
        s.Write(word);
    }

    static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++)
            c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    static uint Crc(ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in data)
            c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
