using TotkCommon;
using Yaz0Sharp;

namespace WildRenderingSharp.Rom;

/// <summary>Yaz0, and the zstd dictionaries Tears of the Kingdom packs its files with.</summary>
public sealed class RomCompression
{
    readonly Zstd _zstd = new();

    public RomCompression()
    {
    }

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
