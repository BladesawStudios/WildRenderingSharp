using System.Collections.Concurrent;
using System.IO.Enumeration;

namespace WildRenderingSharp.Rom;

/// <summary>
/// Folders layered over one another (base game, update, DLC, mods: later folders win) read as one filesystem, with SARC archives opened by
/// path and Yaz0 and zstd understood. Plain files are memory-mapped; decompressed files and opened archives are kept until they add up to
/// more than the cache budget, then the least recently used go.
/// </summary>
public sealed class LayeredRom : IRomAccess
{
    public const long DefaultCacheBudget = 768L << 20;

    const string NestSeparator = "//";

    readonly string[] _roots;
    readonly RomCompression _compression;
    readonly ConcurrentDictionary<string, MappedFile> _mapped = new(StringComparer.OrdinalIgnoreCase);
    readonly SizedLruCache<DecompressedFile> _decompressed;
    readonly AsyncLocal<RomRecording?> _recording = new();

    /// <param name="roots">Lowest priority first.</param>
    /// <param name="cacheBudget">The decompressed bytes to keep, at most, once the newest file is set aside.</param>
    public LayeredRom(IEnumerable<string> roots, RomCompression? compression = null, long cacheBudget = DefaultCacheBudget)
    {
        _roots = roots.Where(Directory.Exists).Reverse().ToArray();
        if (_roots.Length == 0)
            throw new DirectoryNotFoundException("None of the ROM folders exist.");
        _compression = compression ?? new RomCompression();
        _decompressed = new SizedLruCache<DecompressedFile>(cacheBudget, file => file.Bytes.LongLength);
    }

    /// <summary>The folders that exist, highest priority first.</summary>
    public IReadOnlyList<string> Roots => _roots;

    /// <summary>The file on disk that holds <paramref name="path"/>, or the archive that does when it names one inside an archive.</summary>
    public string? Locate(string path) => LocateFile(Split(path).Container);

    /// <summary>Starts noting which on-disk file answers each lookup on this async flow, until the recording is disposed.</summary>
    public RomRecording Record() => new(_recording);

    public bool Exists(string path)
    {
        Note(path);
        var (container, inner) = Split(path);
        if (inner is null)
            return LocateFile(container) is not null;
        return Open(container) is { } archive && archive.Contains(inner);
    }

    public IEnumerable<string> Enumerate(string directory, string searchPattern = "*")
    {
        var (container, inner) = Split(directory);
        return inner is null ? EnumerateFolders(directory, searchPattern) : EnumerateArchive(container, inner, searchPattern);
    }

    public ReadOnlySpan<byte> ReadAllBytesDirectSpan(string path) => Read(path, decompress: false);

    public ReadOnlySpan<byte> ReadAllBytesCompressedSpan(string path)
    {
        ReadOnlySpan<byte> stored = Read(path, decompress: false);
        if (!_compression.IsCompressed(stored))
            throw new InvalidDataException($"'{path}' is not in a compressed format.");
        return Decompressed(path, stored);
    }

    public ReadOnlySpan<byte> ReadAllBytesNested(string path) => Read(path, decompress: true);

    public Stream OpenStreamDirect(string path)
    {
        Note(path);
        var (container, inner) = Split(path);
        if (inner is null && LocateFile(container) is { } file)
            return new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new MemoryStream(Read(path, decompress: false).ToArray(), writable: false);
    }

    IEnumerable<string> EnumerateFolders(string directory, string searchPattern)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in _roots)
        {
            string folder = Path.Combine(root, directory);
            if (!Directory.Exists(folder))
                continue;
            foreach (string file in Directory.EnumerateFiles(folder, searchPattern))
                seen.Add(directory.TrimEnd('/') + "/" + Path.GetFileName(file));
        }
        return seen;
    }

    IEnumerable<string> EnumerateArchive(string container, string inner, string searchPattern)
    {
        string prefix = inner.Length == 0 || inner.EndsWith('/') ? inner : inner + "/";
        var archive = Open(container);
        return archive is null ? [] : archive.Names
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !name[prefix.Length..].Contains('/')
                && Matches(name[prefix.Length..], searchPattern))
            .Select(name => container + NestSeparator + name);
    }

    ReadOnlySpan<byte> Read(string path, bool decompress)
    {
        Note(path);
        var (container, inner) = Split(path);
        ReadOnlySpan<byte> stored;
        if (inner is null)
        {
            string file = LocateFile(container) ?? throw new FileNotFoundException($"'{path}' is in none of the ROM folders.");
            stored = _mapped.GetOrAdd(file, f => new MappedFile(f)).Span;
        }
        else
        {
            var archive = Open(container) ?? throw new FileNotFoundException($"'{container}' is not an archive in the ROM.");
            if (!archive.TryGet(inner, out var entry))
                throw new FileNotFoundException($"'{inner}' is not in '{container}'.");
            stored = entry.AsSpan();
        }
        return decompress && _compression.IsCompressed(stored) ? Decompressed(path, stored) : stored;
    }

    byte[] Decompressed(string key, ReadOnlySpan<byte> stored)
    {
        if (_decompressed.TryGet(key, out var cached))
            return cached.Bytes;
        return _decompressed.Add(key, new DecompressedFile(_compression.Decompress(stored))).Bytes;
    }

    RomArchive? Open(string path)
    {
        if (!_decompressed.TryGet(path, out var file))
        {
            byte[] bytes;
            try
            {
                ReadOnlySpan<byte> stored = Read(path, decompress: false);
                bytes = _compression.IsCompressed(stored) ? _compression.Decompress(stored) : stored.ToArray();
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            file = _decompressed.Add(path, new DecompressedFile(bytes));
        }
        return file.Archive;
    }

    string? LocateFile(string container)
    {
        foreach (string root in _roots)
        {
            string file = Path.Combine(root, container);
            if (File.Exists(file))
                return file;
        }
        return null;
    }

    void Note(string path)
    {
        if (_recording.Value is not { } recording)
            return;
        string normalized = path.Replace('\\', '/');
        int nest = normalized.IndexOf(NestSeparator, StringComparison.Ordinal);
        string onDisk = nest < 0 ? normalized : normalized[..nest];
        recording.Note(onDisk, LocateFile(onDisk));
    }

    static (string Container, string? Inner) Split(string path)
    {
        path = path.Replace('\\', '/');
        int at = path.LastIndexOf(NestSeparator, StringComparison.Ordinal);
        return at < 0 ? (path, null) : (path[..at], path[(at + NestSeparator.Length)..]);
    }

    static bool Matches(string name, string pattern) =>
        pattern == "*" || FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true);

    public void Dispose()
    {
        foreach (var file in _mapped.Values)
            file.Dispose();
        _mapped.Clear();
        _decompressed.Clear();
    }
}
