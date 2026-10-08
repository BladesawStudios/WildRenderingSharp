using System.Runtime.InteropServices;

namespace WildRenderingSharp.Preparation;

/// <summary>Deswizzles a Tegra X1 block-linear BC4 surface with the native <c>tegra_swizzle_x64</c> library CompileTool ships.</summary>
static class Bc4BlockLinear
{
    const int BytesPerBlock = 8;

    [DllImport("tegra_swizzle_x64", EntryPoint = "deswizzle_block_linear")]
    static extern unsafe void DeswizzleBlockLinear(ulong width, ulong height, ulong depth,
        byte* source, ulong sourceLength, byte* destination, ulong destinationLength, ulong blockHeight, ulong bytesPerPixel);

    [DllImport("tegra_swizzle_x64", EntryPoint = "block_height_mip0")]
    static extern ulong BlockHeightMip0(ulong heightInBlocks);

    public static unsafe byte[] Deswizzle(byte[] swizzled, int width, int height)
    {
        ulong widthInBlocks = (ulong)(width + 3) / 4;
        ulong heightInBlocks = (ulong)(height + 3) / 4;
        ulong blockHeight = BlockHeightMip0(heightInBlocks);

        var output = new byte[widthInBlocks * heightInBlocks * BytesPerBlock];
        fixed (byte* source = swizzled)
        fixed (byte* destination = output)
            DeswizzleBlockLinear(widthInBlocks, heightInBlocks, 1, source, (ulong)swizzled.Length,
                destination, (ulong)output.Length, blockHeight, BytesPerBlock);
        return output;
    }
}
