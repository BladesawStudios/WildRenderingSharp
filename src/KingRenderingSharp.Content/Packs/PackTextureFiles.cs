using BfShaderSharp.Errors;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;
using ZsDicSharp;

namespace KingRenderingSharp.Content.Packs;

/// <summary>A pack texture as the renderer's texture files hold it: a format name, the mip chain back to back, and the channel selectors.</summary>
/// <remarks>
/// The format name is the one the uploader reads: BC1, BC3, BC4, BC5, BC7 or ASTC_WxH, then _UNORM, _UNORM_SRGB or _SNORM. Only a texture in linear storage is read; the
/// bytes are the first array slice's levels from the largest down. A format the uploader has no way to put on the GPU (an uncompressed one, or BC2, BC6) comes back as null, and the
/// shape's sampler is written without a file. The record's channel sources are BNTX's (zero 0, one 1, then red 2, green 3, blue 4, alpha 5); the file's selectors are
/// 0 to 3 for red to alpha and 4 and 5 for the constants.
/// </remarks>
sealed class PackTextureFiles(AssetPack pack) : IDisposable
{
    readonly ZsDic _zstd = ZsDic.Dictionaryless();

    public sealed record File(string Format, int Width, int Height, byte[] Bytes, int[] CompSelect);

    public File? Read(string name)
    {
        if (pack.Texture(name) is not { } record) return null;
        if (record.Storage != TextureStorage.Linear) throw new BfShaderUnsupportedException("Only linear texture storage is read; prepare the pack with linear textures");
        if (FormatName(record.Format) is not { } format) return null;
        byte[] all = Decompress(pack.Blob(record.File));
        int levels = Math.Min(record.MipCount, record.LevelSizes.Count);
        long total = 0;
        for (int i = 0; i < levels; i++) total += record.LevelSizes[i];
        if (levels == 0 || total > all.Length) throw new BfShaderFormatException($"Texture {name} has {all.Length} bytes for {total} bytes of levels");
        return new File(format, record.Width, record.Height, all[..(int)total], Selectors(record.Channels));
    }

    byte[] Decompress(BlobData blob)
    {
        if (blob.Codec != BlobCodec.ZstdFrame) return blob.Bytes;
        if (blob.DictionaryId != 0) throw new BfShaderUnsupportedException($"A texture blob is compressed with dictionary {blob.DictionaryId}");
        return _zstd.Decompress(blob.Bytes, ZsDictionary.None);
    }

    // The BNTX surface format word: the layout in the high bytes (BC1 0x1A, BC2 0x1B, BC3 0x1C, BC4 0x1D, BC5 0x1E, BC6 0x1F, BC7 0x20, then ASTC from 4x4 at 0x2D) and the interpretation in the low byte
    // (1 normalized, 2 signed normalized, 6 sRGB). The layouts are written here and not read through TexSharp, whose several copies in one host disagree on the order of their format enum.
    static readonly string[] AstcFootprints = ["4x4", "5x4", "5x5", "6x5", "6x6", "8x5", "8x6", "8x8", "10x5", "10x6", "10x8", "10x10", "12x10", "12x12"];

    public static string? FormatName(uint word)
    {
        uint layout = word >> 8, variant = word & 0xff;
        string? family = layout switch
        {
            0x1A => "BC1",
            0x1C => "BC3",
            0x1D => "BC4",
            0x1E => "BC5",
            0x20 => "BC7",
            >= 0x2D and <= 0x3A => "ASTC_" + AstcFootprints[(int)layout - 0x2D],
            _ => null,
        };
        if (family is null || variant is not (1 or 2 or 6)) return null;
        return family + (variant == 6 ? "_UNORM_SRGB" : variant == 2 ? "_SNORM" : "_UNORM");
    }

    static int[] Selectors(uint channels) => [.. Enumerable.Range(0, 4).Select(i => (int)(channels >> (8 * i) & 0xff) switch { 0 => 4, 1 => 5, var source => source - 2 })];

    public void Dispose() => _zstd.Dispose();
}
