using System.Text.RegularExpressions;
using AstcSharp;
using AstcSharp.Core;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Assets;

/// <summary>
/// Maps a TotK texture's format string (as exported by <c>ExportTestBench</c>/<c>TxtgTexture</c>, e.g. <c>BC1_UNORM</c>,
/// <c>BC7_UNORM</c>, <c>ASTC_4x4_SRGB</c>) to how to get it onto the GPU. <c>ExportTestBench.ExportModel</c> already writes each
/// texture's post-deswizzle, linear block-compressed data straight to a <c>.bin</c> file, so BC1/BC3/BC4/BC5/BC7 need no CPU work
/// at all - just <c>glCompressedTexImage2D</c> with the matching internal format. ASTC is different:
/// <c>GL_KHR_texture_compression_astc_ldr</c> is a genuinely optional extension on desktop GL (several common desktop GPUs/drivers
/// lack it entirely, unlike BC/RGTC/BPTC which desktop GL supports universally), so gambling on hardware support silently produced
/// an undefined/black texture on hardware without it. ASTC is decoded to plain RGBA on the CPU via <c>AstcSharp</c> instead and
/// uploaded as an ordinary uncompressed texture, which works everywhere.
/// </summary>
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

    /// <summary>
    /// Resolves a format string to upload info, or <see langword="null"/> for a format this codebase doesn't (yet) handle - carried
    /// over as an explicit skip rather than a crash, the same gap <c>TxtgTexture.FormatList</c>/<c>load_texture</c> already have
    /// for a handful of formats (e.g. terrain's format code 0xC0C).
    /// </summary>
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

    /// <summary>
    /// Total byte length of a mip level's compressed (or, for ASTC, still-compressed source) data for this format at a given
    /// resolution.
    /// </summary>
    public static int ComputeDataLength(Info info, int width, int height) =>
        ((width + info.BlockWidth - 1) / info.BlockWidth) *
        ((height + info.BlockHeight - 1) / info.BlockHeight) * info.BytesPerBlock;

    /// <summary>Decodes ASTC block data to plain RGBA8 bytes (row-major, no padding) via AstcSharp.</summary>
    public static byte[] DecodeAstc(byte[] blockData, int width, int height, FootprintType footprint, bool srgb)
    {
        using var source = new MemoryStream(blockData);
        using var destination = new MemoryStream();
        AstcDecoder.DecompressImage(source, destination, width, height, Footprint.FromFootprintType(footprint),
            srgb ? LdrDecodeMode.Srgb : LdrDecodeMode.Linear);
        return destination.ToArray();
    }

    /// <summary>
    /// Mirrors <c>load_texture</c>'s sRGB heuristic: an explicit <c>_SRGB</c> suffix, or the sampler/texture naming pattern TotK
    /// uses for albedo-ish inputs.
    /// </summary>
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
