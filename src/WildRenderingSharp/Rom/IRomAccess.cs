namespace WildRenderingSharp.Rom;

/// <summary>
/// Read access to one game's files as a single logical filesystem. An implementation decides what that is made of (base game, update and
/// DLC layers, a host's mod overlays, files already held in memory) and what compression it understands.
/// </summary>
/// <remarks>
/// Paths are game-relative with forward slashes. A double slash marks the start of a path inside an archive:
/// <c>Pack/TitleBG.pack//Model/Link.sbfres</c> is <c>Model/Link.sbfres</c> inside the SARC <c>Pack/TitleBG.pack</c>, which is
/// decompressed first when it needs to be. A returned span stays valid until the implementation is disposed, and may point at
/// memory-mapped or cached data, so it must not be written to.
/// </remarks>
public interface IRomAccess : IDisposable
{
    bool Exists(string path);

    /// <summary>The names of the files directly under <paramref name="directory"/> (a directory, or an archive path ending in a directory), as full paths.</summary>
    IEnumerable<string> Enumerate(string directory, string searchPattern = "*");

    /// <summary>The whole file as stored.</summary>
    ReadOnlySpan<byte> ReadAllBytesDirectSpan(string path);

    /// <summary>The file as stored, as a stream, for decompressing on the fly or reading a small part of a large file.</summary>
    Stream OpenStreamDirect(string path);

    /// <summary>The whole file decompressed. Throws <see cref="InvalidDataException"/> when the file is not in a compressed format the implementation knows.</summary>
    ReadOnlySpan<byte> ReadAllBytesCompressedSpan(string path);

    /// <summary>The whole file, found through whatever archives <paramref name="path"/> names, decompressed if it is compressed.</summary>
    ReadOnlySpan<byte> ReadAllBytesNested(string path);
}
