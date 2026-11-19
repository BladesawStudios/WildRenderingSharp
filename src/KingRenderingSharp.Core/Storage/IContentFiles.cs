namespace KingRenderingSharp.Core.Storage;

/// <summary>Named files of the content a renderer draws from, wherever they are kept: a folder, or an asset pack's entries.</summary>
/// <remarks>A name is a plain file name (<c>terrain_prog2_extracted.frag</c>); what it means to a store is the store's own. A store that cannot give a file says so by <see cref="Exists"/>.</remarks>
public interface IContentFiles
{
    bool Exists(string name);

    /// <summary>The bytes of a file; throws <see cref="FileNotFoundException"/> when the store has none by that name.</summary>
    byte[] ReadBytes(string name);

    /// <summary>The names that match a pattern with at most one <c>*</c> in it.</summary>
    IEnumerable<string> Enumerate(string pattern);

    /// <summary>Something that changes when the file does, for a cache keyed by the file's identity.</summary>
    string Stamp(string name);
}

public static class ContentFilesExtensions
{
    public static string ReadText(this IContentFiles files, string name) => System.Text.Encoding.UTF8.GetString(files.ReadBytes(name));

    public static string? TryReadText(this IContentFiles files, string name) => files.Exists(name) ? files.ReadText(name) : null;
}

/// <summary>The files of a folder. A folder that is not there holds no files.</summary>
public sealed class FolderContentFiles(string directory) : IContentFiles
{
    public string Directory { get; } = directory;

    public bool Exists(string name) => CacheFiles.Exists(Path.Combine(Directory, name));

    public byte[] ReadBytes(string name) => CacheFiles.ReadAllBytes(Path.Combine(Directory, name));

    public IEnumerable<string> Enumerate(string pattern) =>
        System.IO.Directory.Exists(Directory) ? System.IO.Directory.EnumerateFiles(Directory, pattern).Select(f => Path.GetFileName(f)!) : [];

    public string Stamp(string name)
    {
        var info = new FileInfo(Path.Combine(Directory, name));
        return info.Exists ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}" : "absent";
    }
}
