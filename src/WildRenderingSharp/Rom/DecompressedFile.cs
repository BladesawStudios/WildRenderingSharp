using SarcLibrary;

namespace WildRenderingSharp.Rom;

/// <summary>A file's decompressed bytes, and the archive read from them when they are one.</summary>
sealed class DecompressedFile(byte[] bytes)
{
    readonly Lazy<RomArchive?> _archive = new(() => IsSarc(bytes) ? new RomArchive(Sarc.FromBinary(new ArraySegment<byte>(bytes))) : null);

    public byte[] Bytes => bytes;
    public RomArchive? Archive => _archive.Value;

    static bool IsSarc(byte[] data) => data.Length >= 4 && data[0] == 'S' && data[1] == 'A' && data[2] == 'R' && data[3] == 'C';
}

/// <summary>The entries of one SARC by name, ignoring case.</summary>
sealed class RomArchive(Sarc sarc)
{
    public IReadOnlyDictionary<string, ArraySegment<byte>> Files { get; } = sarc.ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
}
