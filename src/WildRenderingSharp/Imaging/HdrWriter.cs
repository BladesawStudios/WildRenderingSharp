namespace WildRenderingSharp.Imaging;

/// <summary>
/// Writes a top-down RGBA float buffer (as produced by <c>RenderTargets.ReadPixelsFloatRgba</c>) as a Radiance RGBE (.hdr) file -
/// the standard, widely-supported HDR image format, hand-written here since nothing in this codebase encoded one before. Format
/// reference: the classic Radiance picture format (used by Blender, Photoshop, every HDR viewer) - a short ASCII header followed by
/// scanlines of 4-byte RGBE (mantissa R/G/B + a shared power-of-two exponent byte), uncompressed (the "old" flat encoding, not the
/// newer RLE scanline compression - simpler and universally readable, at the cost of a slightly larger file).
/// </summary>
public static class HdrWriter
{
    public static void WriteRgbaFloat(string path, int width, int height, float[] rgba)
    {
        using var stream = File.Create(path);
        using var writer = new StreamWriter(stream, System.Text.Encoding.ASCII);
        writer.Write("#?RADIANCE\n");
        writer.Write("FORMAT=32-bit_rle_rgbe\n\n");
        writer.Write($"-Y {height} +X {width}\n");
        writer.Flush();

        var row = new byte[width * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                var (r, g, b, e) = EncodeRgbe(rgba[i], rgba[i + 1], rgba[i + 2]);
                row[x * 4 + 0] = r;
                row[x * 4 + 1] = g;
                row[x * 4 + 2] = b;
                row[x * 4 + 3] = e;
            }
            stream.Write(row, 0, row.Length);
        }
    }

    static (byte r, byte g, byte b, byte e) EncodeRgbe(float r, float g, float b)
    {
        float max = MathF.Max(r, MathF.Max(g, b));
        if (max < 1e-32f)
            return (0, 0, 0, 0);

        // Radiance's own encoding: mantissa in [0.5, 1.0) scaled into a byte, plus a shared
        // exponent (biased by 128) so R/G/B/exponent recombine as value = mantissa * 2^(exp-128).
        int exp = (int)MathF.Floor(MathF.Log2(max)) + 1;
        float scale = MathF.Pow(2f, -exp) * 256f;
        byte re = (byte)Math.Clamp(r * scale, 0f, 255f);
        byte ge = (byte)Math.Clamp(g * scale, 0f, 255f);
        byte be = (byte)Math.Clamp(b * scale, 0f, 255f);
        byte ee = (byte)Math.Clamp(exp + 128, 0, 255);
        return (re, ge, be, ee);
    }
}
