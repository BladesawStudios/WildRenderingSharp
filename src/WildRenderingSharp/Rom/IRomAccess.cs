namespace WildRenderingSharp.Rom;

/// <summary>Read access to one game's files as a single filesystem. Paths are game-relative with forward slashes, and `//` starts a path inside an archive, as in `Pack/TitleBG.pack//Model/Link.sbfres`.</summary>
public interface IRomAccess : IDisposable
{
    bool Exists(string path);

    IEnumerable<string> Enumerate(string directory, string searchPattern = "*");

    ReadOnlySpan<byte> ReadAllBytesDirectSpan(string path);

    Stream OpenStreamDirect(string path);

    ReadOnlySpan<byte> ReadAllBytesCompressedSpan(string path);

    ReadOnlySpan<byte> ReadAllBytesNested(string path);
}
