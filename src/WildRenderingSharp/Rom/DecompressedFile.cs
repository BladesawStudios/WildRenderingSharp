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

/// <summary>The entries of one SARC by name: an exact match wins, then one that differs only in case.</summary>
sealed class RomArchive
{
    readonly Dictionary<string, ArraySegment<byte>> _exact = new(StringComparer.Ordinal);
    readonly Dictionary<string, ArraySegment<byte>> _ignoringCase = new(StringComparer.OrdinalIgnoreCase);

    public RomArchive(Sarc sarc)
    {
        foreach (var (name, data) in sarc)
        {
            _exact[name] = data;
            _ignoringCase.TryAdd(name, data);
        }
    }

    public IEnumerable<string> Names => _exact.Keys;

    public bool TryGet(string name, out ArraySegment<byte> data) => _exact.TryGetValue(name, out data) || _ignoringCase.TryGetValue(name, out data);

    public bool Contains(string name) => TryGet(name, out _);
}
