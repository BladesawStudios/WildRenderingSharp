using System.Collections.Concurrent;
using System.IO.Hashing;

namespace WildRenderingSharp.Storage;

/// <summary>Reads and packs the big binary files of a prepared model: vertices, indices, material blocks and textures.</summary>
/// <remarks>
/// A file <c>X</c> packed in place becomes <c>X.zst</c>, compressed with zstd. A texture becomes <c>_textures/&lt;hash&gt;.zst</c> in the cache root, kept once however many
/// models use it, and the model's folder keeps a small <c>textures.idx</c> saying which hash each of its texture files was. JSON and text files stay as they are.
/// Everything that reads one of these files goes through <see cref="ReadAllBytes"/>, which takes either form, so a cache that is part packed, or packed while a host reads it, works.
/// A model prepared inside a <see cref="ModelSession"/> keeps all of its small files, and its texture index, in one <see cref="ModelBundle"/> instead; the readers here look in
/// a model's bundle first and in its folder after, so a cache of both kinds is read the same way.
/// </remarks>
public static class CacheFiles
{
    // The folder of the cache root that holds every texture once.
    public const string TextureFolder = "_textures";

    const string Suffix = ".zst";
    const string TextureIndex = "textures.idx";

    // Fast to write and faster to read; the cache is read far more often than it is made.
    const int Level = 3;

    [ThreadStatic] static ZstdSharp.Compressor? _compressor;
    [ThreadStatic] static ZstdSharp.Decompressor? _decompressor;

    // Whether a model is prepared: its bundle is written, which is only once all of it is, or its manifest is in its folder as a file. Asks the disk about one file and reads nothing.
    public static bool IsModelPrepared(string folder, string manifestName) =>
        File.Exists(Path.Combine(folder, ModelBundle.FileName)) || File.Exists(Path.Combine(folder, manifestName));

    // The names of the files in a model's folder that match the pattern, whether they are in its bundle, in the open session or in the folder. The pattern has one * in it at most.
    public static IEnumerable<string> EnumerateFiles(string folder, string pattern)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (ModelSession.Current is { } session && session.NameOf(Path.Combine(folder, "x")) is not null)
            foreach (string held in session.Names)
                names.Add(held);
        if (ModelBundles.OpenIn(folder) is { } bundle)
            foreach (string stored in bundle.Names)
                names.Add(stored);
        if (Directory.Exists(folder))
            foreach (string file in Directory.EnumerateFiles(folder, pattern))
                names.Add(Path.GetFileName(file));

        int star = pattern.IndexOf('*');
        string head = star < 0 ? pattern : pattern[..star], tail = star < 0 ? "" : pattern[(star + 1)..];
        return names.Where(n => !n.Contains('/') && (star < 0 ? n == pattern : n.Length >= head.Length + tail.Length
            && n.StartsWith(head, StringComparison.Ordinal) && n.EndsWith(tail, StringComparison.Ordinal)));
    }

    // Opens the session that collects a model's small files, to be written as one bundle by Pack. keepExisting starts it from the bundle the folder has, for redoing part of a model.
    public static ModelSession BeginModel(string folder, bool keepExisting) => ModelSession.Begin(folder, keepExisting);

    // Whether the file is there, in whichever form.
    public static bool Exists(string path) => FileExists(path) || File.Exists(path + Suffix) || SharedTexture(path) is not null;

    // Makes a model's folder for a file that is to be written into it. A folder whose files go into the open session's bundle is made when the bundle is written, so
    // nothing is made for it now.
    public static void EnsureFolder(string path)
    {
        if (InSession(Path.Combine(path, "x")) is null)
            Directory.CreateDirectory(path);
    }

    // Whether the file is in the open session, in the model's bundle, or in its folder as it is.
    public static bool FileExists(string path)
    {
        if (InSession(path) is { } held)
            return held.Contains(held.NameOf(path)!);
        return ModelBundles.Locate(path) is not null || File.Exists(path);
    }

    // The text of a small file of a model, from its bundle or its folder; null when it has none.
    public static string? TryReadText(string path)
    {
        if (InSession(path) is { } held && held.Read(held.NameOf(path)!) is { } fromSession)
            return System.Text.Encoding.UTF8.GetString(fromSession);
        if (ModelBundles.Locate(path) is var (bundle, name))
            return bundle.Read(name) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : null;
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    // Writes a small text file of a model: into the open session when the file is in its folder, otherwise to the folder.
    public static void WriteText(string path, string text)
    {
        if (InSession(path) is { } held)
            held.Add(held.NameOf(path)!, System.Text.Encoding.UTF8.GetBytes(text), packed: false);
        else
            File.WriteAllText(path, text);
    }

    // The open session, when the file is inside its model's folder.
    static ModelSession? InSession(string path) => ModelSession.Current is { } session && session.NameOf(path) is not null ? session : null;

    // The file's bytes, from the plain file, the compressed one beside it, or the cache's shared copy of a texture.
    public static byte[] ReadAllBytes(string path)
    {
        if (InSession(path) is { } held && held.Read(held.NameOf(path)!) is { } fromSession)
            return fromSession;
        if (ModelBundles.Locate(path) is var (bundle, name) && bundle.Read(name) is { } fromBundle)
            return fromBundle;

        // The plain file may be deleted between the check and the read by a pack running beside the reader, so each form is tried, not asked about.
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Not stored plain: the packed form is tried next.
        }

        try
        {
            return Unpack(File.ReadAllBytes(path + Suffix));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Not packed beside it either: a shared texture is tried next.
        }

        if (SharedTexture(path) is { } shared)
            return Unpack(File.ReadAllBytes(shared));
        throw new FileNotFoundException($"'{path}' is not in the cache, plain, compressed or shared.", path);
    }

    // The textures written into a model's folder by WriteBytes, by folder, until Pack writes the folder's index.
    static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> PendingIndex = new(StringComparer.OrdinalIgnoreCase);

    // Writes one of a model's binary files straight into its packed form: compressed beside where it would have been, or, for a texture, into the shared folder with its
    // place in the model's index kept for Pack to write.
    public static void WriteBytes(string path, byte[] bytes)
    {
        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full)!;
        string name = Path.GetFileName(full);
        bool material = string.Equals(Path.GetFileName(directory), "matubo", StringComparison.OrdinalIgnoreCase);

        if (material || IsGeometryPart(name) || !name.EndsWith(".bin", StringComparison.Ordinal))
        {
            if (InSession(full) is { } held)
                held.Add(held.NameOf(full)!, Compress(bytes), packed: true);
            else
                File.WriteAllBytes(full + Suffix, Compress(bytes));
        }
        else
        {
            string folder = Path.Combine(Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))!, TextureFolder);
            string hash = ContentHash(bytes);
            string target = Path.Combine(folder, hash + Suffix);
            if (!File.Exists(target))
                WriteShared(target, CompressTexture(bytes));
            PendingIndex.GetOrAdd(directory, _ => new(StringComparer.Ordinal))[name] = hash;
        }

        // A plain file from an earlier preparation of this folder would be read in preference to what was just written. Deleting one that is not there does nothing. A session's
        // bundle is read before the folder, so it needs no deleting.
        if (InSession(full) is null)
            File.Delete(full);
    }

    // Writes a shared texture, which is named by what is in it. Two models that use the same texture can be prepared at the same moment, each finding the file not there yet and
    // each moving its copy into place, and the move that comes second is refused while the first has the file. The file is there and is the same one, so that is not a failure:
    // it used to drop the texture from the model, which was skipped as unreadable, and which textures went missing changed from one preparation to the next.
    static void WriteShared(string target, byte[] compressed)
    {
        try
        {
            AtomicFile.WriteAllBytes(target, compressed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && File.Exists(target))
        {
            // Another thread put the same file there first.
        }
    }

    // What a texture's bytes are called in the shared folder. A texture is hashed once for each model that uses it and is megabytes, so this is a fast hash that does not
    // collide in practice; a cryptographic one was a tenth of the time a cold cache took to make.
    static string ContentHash(byte[] bytes) => Convert.ToHexString(XxHash128.Hash(bytes));

    // Reads a file and throws the bytes away, so the system has it in memory when the real read comes. Many of these at once cost no more than one, since a disk answers
    // many requests at a time.
    public static void WarmFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1 << 16);
            try
            {
                while (stream.Read(buffer, 0, buffer.Length) > 0) { }
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be read is simply not warmed, and is read when it is wanted.
        }
    }

    // Warms every file of a model's folder, and the shared textures its index names.
    public static void WarmModel(string modelDirectory)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(modelDirectory, "*", SearchOption.AllDirectories))
                WarmFile(file);

            string? root = Path.GetDirectoryName(Path.GetFullPath(modelDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (root is not null)
                foreach (string hash in IndexOf(modelDirectory).Values.Distinct())
                    WarmFile(Path.Combine(root, TextureFolder, hash + Suffix));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A model whose folder cannot be listed is not warmed.
        }
    }

    // The hash a texture written to a model's folder by WriteBytes went into the shared folder under, or null.
    public static string? WrittenHash(string modelDirectory, string fileName) =>
        PendingIndex.TryGetValue(Path.GetFullPath(modelDirectory), out var written) && written.TryGetValue(fileName, out string? hash) ? hash : null;

    // Gives a model's folder a texture that is already in the shared folder, as if it had been written. False when the shared folder does not have it.
    public static bool LinkTexture(string modelDirectory, string fileName, string hash)
    {
        string directory = Path.GetFullPath(modelDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(directory)!, TextureFolder, hash + Suffix)))
            return false;
        PendingIndex.GetOrAdd(directory, _ => new(StringComparer.Ordinal))[fileName] = hash;
        if (ModelSession.Current is null)
            File.Delete(Path.Combine(directory, fileName));
        return true;
    }

    // Packs a model's folder: compresses its vertex, index, shape, context and material-block files in place, and moves its textures into the shared folder. Safe to run
    // again, and while the model is being read.
    public static void Pack(string modelDirectory)
    {
        string directory = Path.GetFullPath(modelDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (ModelSession.Current is { } session && session.NameOf(Path.Combine(directory, "x")) is not null)
        {
            Commit(session, directory);
            return;
        }
        string shared = Path.Combine(Path.GetDirectoryName(directory)!, TextureFolder);

        var textures = Directory.EnumerateFiles(directory, "*.bin").Where(f => !IsGeometryPart(f)).ToList();
        PendingIndex.TryRemove(directory, out var written);
        if (textures.Count > 0 || written is { Count: > 0 })
        {
            Directory.CreateDirectory(shared);
            var index = ReadIndex(Path.Combine(directory, TextureIndex));
            if (written is not null)
                foreach (var (file, hash) in written)
                    index[file] = hash;
            foreach (string file in textures)
            {
                byte[] bytes = File.ReadAllBytes(file);
                string hash = ContentHash(bytes);
                string target = Path.Combine(shared, hash + Suffix);
                if (!File.Exists(target))
                    WriteShared(target, Compress(bytes));
                index[Path.GetFileName(file)] = hash;
            }

            // The index goes down before any texture file goes, so a reader that finds one missing finds where it went.
            AtomicFile.WriteAllText(Path.Combine(directory, TextureIndex), string.Join('\n', index.Select(e => $"{e.Key}\t{e.Value}")));
            foreach (string file in textures)
                File.Delete(file);
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*.bin").Where(IsGeometryPart).ToList())
            CompressInPlace(file);

        string materials = Path.Combine(directory, "matubo");
        if (Directory.Exists(materials))
            foreach (string file in Directory.EnumerateFiles(materials, "*.bin").ToList())
                CompressInPlace(file);
    }

    // Written straight to its place, not aside and moved: the plain file is read first and is deleted only once this is whole, so a reader never takes half of it.
    static void CompressInPlace(string file)
    {
        File.WriteAllBytes(file + Suffix, Compress(File.ReadAllBytes(file)));
        File.Delete(file);
    }

    // The files a shape is made of; any other .bin in a model's folder is a texture.
    static bool IsGeometryPart(string file) =>
        file.EndsWith("_vtx.bin", StringComparison.Ordinal) || file.EndsWith("_idx.bin", StringComparison.Ordinal)
        || file.EndsWith("_shape.bin", StringComparison.Ordinal) || file.EndsWith("_context.bin", StringComparison.Ordinal);

    static string? SharedTexture(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory is null)
            return null;
        if (!IndexOf(directory).TryGetValue(Path.GetFileName(path), out string? hash))
            return null;
        string? root = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (root is null)
            return null;
        string shared = Path.Combine(root, TextureFolder, hash + Suffix);
        return File.Exists(shared) ? shared : null;
    }

    // The texture index of a model's folder, from the open session, its bundle or the folder; empty when it has none. Read afresh each time for a folder's own file, and once
    // for a bundle, whose entry never changes.
    static IReadOnlyDictionary<string, string> IndexOf(string folder)
    {
        string path = Path.Combine(folder, TextureIndex);
        if (InSession(path) is { } held)
            return held.Read(TextureIndex) is { } fromSession ? ParseIndex(fromSession) : new Dictionary<string, string>();
        if (ModelBundles.OpenIn(folder) is { } bundle)
            return ParsedIndexes.GetValue(bundle, b => b.Read(TextureIndex) is { } bytes ? ParseIndex(bytes) : new Dictionary<string, string>());
        return ReadIndex(path);
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ModelBundle, Dictionary<string, string>> ParsedIndexes = new();

    static Dictionary<string, string> ParseIndex(byte[] bytes)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in System.Text.Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            int tab = line.IndexOf('\t');
            if (tab > 0)
                index[line[..tab]] = line[(tab + 1)..].TrimEnd('\r');
        }
        return index;
    }

    static Dictionary<string, string> ReadIndex(string indexPath)
    {
        try
        {
            return File.Exists(indexPath) ? ParseIndex(File.ReadAllBytes(indexPath)) : new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // A folder with no index has no shared textures, which is an empty index.
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    // Writes the session's bundle with the model's texture index in it: what the folder already named, then what this preparation wrote or linked.
    static void Commit(ModelSession session, string directory)
    {
        PendingIndex.TryRemove(directory, out var written);
        var index = new Dictionary<string, string>(IndexOf(directory), StringComparer.Ordinal);
        if (written is not null)
            foreach (var (file, hash) in written)
                index[file] = hash;
        if (index.Count > 0)
            session.Add(TextureIndex, System.Text.Encoding.UTF8.GetBytes(string.Join('\n', index.Select(e => $"{e.Key}\t{e.Value}"))), packed: false);
        session.Commit();
    }

    static byte[] Compress(byte[] bytes) => (_compressor ??= new ZstdSharp.Compressor(Level)).Wrap(bytes).ToArray();

    // Texture data is block-compressed already and gains little from more effort.
    [ThreadStatic] static ZstdSharp.Compressor? _quickCompressor;
    static byte[] CompressTexture(byte[] bytes) => (_quickCompressor ??= new ZstdSharp.Compressor(1)).Wrap(bytes).ToArray();

    internal static byte[] Unpack(ReadOnlySpan<byte> bytes) => (_decompressor ??= new ZstdSharp.Decompressor()).Unwrap(bytes).ToArray();
}
