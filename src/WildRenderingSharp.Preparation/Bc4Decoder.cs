namespace WildRenderingSharp.Preparation;

/// <summary>Decodes BC4_UNORM blocks to one byte per texel.</summary>
static class Bc4Decoder
{
    public static byte[] Decode(byte[] blocks, int width, int height)
    {
        int blocksX = (width + 3) / 4;
        var output = new byte[width * height];
        Span<byte> palette = stackalloc byte[8];

        for (int block = 0; block < blocks.Length / 8; block++)
        {
            int offset = block * 8;
            BuildPalette(blocks[offset], blocks[offset + 1], palette);

            ulong indices = 0;
            for (int i = 0; i < 6; i++)
                indices |= (ulong)blocks[offset + 2 + i] << (8 * i);

            int x0 = block % blocksX * 4, y0 = block / blocksX * 4;
            for (int texel = 0; texel < 16; texel++)
            {
                int x = x0 + texel % 4, y = y0 + texel / 4;
                if (x < width && y < height)
                    output[y * width + x] = palette[(int)((indices >> (3 * texel)) & 7)];
            }
        }
        return output;
    }

    static void BuildPalette(byte r0, byte r1, Span<byte> palette)
    {
        palette[0] = r0;
        palette[1] = r1;
        if (r0 > r1)
        {
            for (int i = 1; i <= 6; i++)
                palette[1 + i] = (byte)(((7 - i) * r0 + i * r1) / 7);
            return;
        }

        for (int i = 1; i <= 4; i++)
            palette[1 + i] = (byte)(((5 - i) * r0 + i * r1) / 5);
        palette[6] = 0;
        palette[7] = 255;
    }
}
