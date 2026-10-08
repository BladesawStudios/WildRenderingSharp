using System.Text.RegularExpressions;
using AstcSharp;
using AstcSharp.Core;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Assets;

/// <summary>Maps a TotK texture's format string (as exported by <c>ExportTestBench</c>/<c>TxtgTexture</c>, e.g.</summary>
public static partial class CompressedTextureFormat
{
    /// <summary>
    /// <see cref="AstcFootprint"/> is set only for ASTC formats - <see cref="TextureCache"/> uses that to decode on the CPU instead
    /// of calling <c>glCompressedTexImage2D</c> with <see cref="Format"/>/<see cref="FormatSrgb"/> (which for ASTC are the plain,
    /// uncompressed RGBA targets the decoded bytes upload to, not a compressed internal format).
    /// </summary>
    public readonly record struct Info(InternalFormat Format, InternalFormat FormatSrgb, int BlockWidth, int BlockHeight, int BytesPerBlock, FootprintType? AstcFootprint);

    [GeneratedRegex(@"^ASTC_(\d+)x(\d+)")]
    private static partial Regex AstcPattern();

    public static Info? Resolve(string format)
    {
        if (format.StartsWith("BC1", StringComparison.Ordinal))
            return new Info(InternalFormat.CompressedRgbaS3TCDxt1Ext, InternalFormat.CompressedSrgbAlphaS3TCDxt1Ext, 4, 4, 8, null);
        if (format.StartsWith("BC3", StringComparison.Ordinal))
            return new Info(InternalFormat.CompressedRgbaS3TCDxt5Ext, InternalFormat.CompressedSrgbAlphaS3TCDxt5Ext, 4, 4, 16, null);
        if (format.StartsWith("BC4", StringComparison.Ordinal))
            return new Info(InternalFormat.CompressedRedRgtc1, InternalFormat.CompressedRedRgtc1, 4, 4, 8, null); // no sRGB variant exists for a single-channel mask
        if (format.StartsWith("BC5", StringComparison.Ordinal))
            return new Info(InternalFormat.CompressedRGRgtc2, InternalFormat.CompressedRGRgtc2, 4, 4, 16, null); // no sRGB variant
        if (format.StartsWith("BC7", StringComparison.Ordinal))
            return new Info(InternalFormat.CompressedRgbaBptcUnorm, InternalFormat.CompressedSrgbAlphaBptcUnorm, 4, 4, 16, null);

        var astc = AstcPattern().Match(format);
        if (astc.Success)
        {
            int bw = int.Parse(astc.Groups[1].Value);
            int bh = int.Parse(astc.Groups[2].Value);
            var footprint = AstcFootprintOf(bw, bh);
            if (footprint is not null)
                return new Info(InternalFormat.Rgba8, InternalFormat.Srgb8Alpha8, bw, bh, 16, footprint); // ASTC is always 128 bits/block regardless of footprint
        }
        return null;
    }

    public static int ComputeDataLength(Info info, int width, int height) =>
        ((width + info.BlockWidth - 1) / info.BlockWidth) *
        ((height + info.BlockHeight - 1) / info.BlockHeight) * info.BytesPerBlock;

    public static byte[] DecodeAstc(byte[] blockData, int width, int height, FootprintType footprint, bool srgb)
    {
        using var source = new MemoryStream(blockData);
        using var destination = new MemoryStream();
        AstcDecoder.DecompressImage(source, destination, width, height, Footprint.FromFootprintType(footprint),
            srgb ? LdrDecodeMode.Srgb : LdrDecodeMode.Linear);
        return destination.ToArray();
    }

    public static bool IsSrgb(string format, string samplerKey, string samplerAssigned, string textureName) =>
        format.EndsWith("_SRGB", StringComparison.Ordinal) ||
        samplerKey is "_a0" or "_a1" or "_a2" or "_fx0" or "_fx1" ||
        samplerAssigned is "_a0" or "_a1" or "_a2" or "_fx0" or "_fx1" ||
        textureName.Contains("_Alb", StringComparison.Ordinal);

    static FootprintType? AstcFootprintOf(int blockW, int blockH) => (blockW, blockH) switch
    {
        (4, 4) => FootprintType.Footprint4x4,
        (5, 4) => FootprintType.Footprint5x4,
        (5, 5) => FootprintType.Footprint5x5,
        (6, 5) => FootprintType.Footprint6x5,
        (6, 6) => FootprintType.Footprint6x6,
        (8, 5) => FootprintType.Footprint8x5,
        (8, 6) => FootprintType.Footprint8x6,
        (8, 8) => FootprintType.Footprint8x8,
        (10, 5) => FootprintType.Footprint10x5,
        (10, 6) => FootprintType.Footprint10x6,
        (10, 8) => FootprintType.Footprint10x8,
        (10, 10) => FootprintType.Footprint10x10,
        (12, 10) => FootprintType.Footprint12x10,
        (12, 12) => FootprintType.Footprint12x12,
        _ => null,
    };
}
