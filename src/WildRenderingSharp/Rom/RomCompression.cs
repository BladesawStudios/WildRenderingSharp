using TotkCommon;
using Yaz0Sharp;

namespace WildRenderingSharp.Rom;

/// <summary>The compression formats the Wild games ship their files in: Yaz0, and zstd with the dictionaries Tears of the Kingdom packs its own.</summary>
public sealed class RomCompression
{
    readonly Zstd _zstd = new();

    public RomCompression()
    {
    }

    /// <param name="dictionaryPack">The game's <c>Pack/ZsDic.pack.zs</c>; a missing file leaves zstd without dictionaries.</param>
    public RomCompression(string dictionaryPack)
    {
        if (File.Exists(dictionaryPack))
            _zstd.LoadDictionaries(dictionaryPack);
    }

    public bool IsCompressed(ReadOnlySpan<byte> data) => Yaz0.IsCompressed(data) || Zstd.IsCompressed(data);

    public byte[] Decompress(ReadOnlySpan<byte> data)
    {
        if (Yaz0.IsCompressed(data))
            return Yaz0.Decompress(data);
        if (!Zstd.IsCompressed(data))
            return data.ToArray();
        lock (_zstd)
            return _zstd.Decompress(data);
    }
}
