namespace WildRenderingSharp.Storage;

/// <summary>The files of one model being prepared, collected in memory on the thread preparing it and written as a single bundle when it is done.</summary>
/// <remarks>
/// While a session is open on a thread, the small files of its model's folder (shape blocks, vertex and index data, options, material blocks and layouts) go into the session
/// instead of onto the disk, and are read back from it; commit writes them as one <see cref="ModelBundle"/>. A session left without a commit writes nothing, so a preparation
/// that failed leaves the model as it was. A session opened to redo part of a model starts from the bundle already there, and one that redoes all of it starts empty.
/// </remarks>
public sealed class ModelSession : IDisposable
{
    [ThreadStatic] static ModelSession? _current;

    readonly Dictionary<string, (byte[] Bytes, bool Packed)> _entries = new(StringComparer.Ordinal);
    readonly string _folder;

    ModelSession(string folder, bool keepExisting)
    {
        _folder = Normalize(folder);
        if (keepExisting && ModelBundle.Open(Path.Combine(_folder, ModelBundle.FileName)) is { } existing)
        {
            foreach (string name in existing.Names)
                if (existing.ReadStored(name) is { } stored)
                    _entries[name] = stored;
        }
        _current = this;
    }

    // The session open on this thread, or null.
    internal static ModelSession? Current => _current;

    // Opens a session for the model whose folder this is. keepExisting starts it from the bundle the folder already has.
    public static ModelSession Begin(string folder, bool keepExisting) => new(folder, keepExisting);

    // The name an entry has in the bundle for a file at path, or null when the file is not in this model's folder.
    internal string? NameOf(string path)
    {
        string full = Normalize(path);
        return full.Length > _folder.Length + 1 && full.StartsWith(_folder, StringComparison.OrdinalIgnoreCase) && full[_folder.Length] == '/'
            ? full[(_folder.Length + 1)..]
            : null;
    }

    internal string Folder => _folder;

    internal void Add(string name, byte[] bytes, bool packed) => _entries[name] = (bytes, packed);

    internal bool Contains(string name) => _entries.ContainsKey(name);

    internal IEnumerable<string> Names => _entries.Keys;

    internal byte[]? Read(string name) =>
        _entries.TryGetValue(name, out var entry) ? entry.Packed ? CacheCodec.Unpack(entry.Bytes) : entry.Bytes : null;

    // Writes the bundle. The folder is made if it is not there, and what is known of it is forgotten so a reader asks the disk.
    internal void Commit()
    {
        Directory.CreateDirectory(_folder);
        ModelBundle.Write(Path.Combine(_folder, ModelBundle.FileName), _entries);
        ModelBundles.Forget(_folder);
    }

    public void Dispose()
    {
        if (ReferenceEquals(_current, this))
            _current = null;
    }

    // Forward slashes and no trailing separator, so a folder and the files in it compare by prefix on any platform.
    static string Normalize(string path) => Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
}
