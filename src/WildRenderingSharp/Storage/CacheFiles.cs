using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace WildRenderingSharp.Storage;

/// <summary>Reads and packs the big binary files of a prepared model: vertices, indices, material blocks and textures.</summary>
/// <remarks>
/// A file <c>X</c> packed in place becomes <c>X.zst</c>, compressed with zstd. A texture becomes <c>_textures/&lt;hash&gt;.zst</c> in the cache root, kept once however many
/// models use it, and the model's folder keeps a small <c>textures.idx</c> saying which hash each of its texture files was. JSON and text files stay as they are.
/// Everything that reads one of these files goes through <see cref="ReadAllBytes"/>, which takes either form, so a cache that is part packed, or packed while a host reads it, works.
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

    // Whether the file is there, in whichever form.
    public static bool Exists(string path) => File.Exists(path) || File.Exists(path + Suffix) || SharedTexture(path) is not null;

    // The file's bytes, from the plain file, the compressed one beside it, or the cache's shared copy of a texture.
    public static byte[] ReadAllBytes(string path)
    {
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
            File.WriteAllBytes(full + Suffix, Compress(bytes));
        }
        else
        {
            string folder = Path.Combine(Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))!, TextureFolder);
            Directory.CreateDirectory(folder);
            string hash = Convert.ToHexString(SHA256.HashData(bytes), 0, 16);
            string target = Path.Combine(folder, hash + Suffix);
            if (!File.Exists(target))
                AtomicFile.WriteAllBytes(target, CompressTexture(bytes));
            PendingIndex.GetOrAdd(directory, _ => new(StringComparer.Ordinal))[name] = hash;
        }

        // A plain file from an earlier preparation of this folder would be read in preference to what was just written.
        if (File.Exists(full))
            File.Delete(full);
    }

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

            string index = Path.Combine(modelDirectory, TextureIndex);
            string? root = Path.GetDirectoryName(Path.GetFullPath(modelDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (root is not null && File.Exists(index))
                foreach (string hash in ReadIndex(index).Values.Distinct())
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
        string plain = Path.Combine(directory, fileName);
        if (File.Exists(plain))
            File.Delete(plain);
        return true;
    }

    // Packs a model's folder: compresses its vertex, index, shape, context and material-block files in place, and moves its textures into the shared folder. Safe to run
    // again, and while the model is being read.
    public static void Pack(string modelDirectory)
    {
        string directory = Path.GetFullPath(modelDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
                string hash = Convert.ToHexString(SHA256.HashData(bytes), 0, 16);
                string target = Path.Combine(shared, hash + Suffix);
                if (!File.Exists(target))
                    AtomicFile.WriteAllBytes(target, Compress(bytes));
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
        string indexPath = Path.Combine(directory, TextureIndex);
        if (!File.Exists(indexPath))
            return null;
        if (!ReadIndex(indexPath).TryGetValue(Path.GetFileName(path), out string? hash))
            return null;
        string? root = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (root is null)
            return null;
        string shared = Path.Combine(root, TextureFolder, hash + Suffix);
        return File.Exists(shared) ? shared : null;
    }

    static Dictionary<string, string> ReadIndex(string indexPath)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (string line in File.ReadLines(indexPath))
            {
                int tab = line.IndexOf('\t');
                if (tab > 0)
                    index[line[..tab]] = line[(tab + 1)..];
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // A folder with no index has no shared textures, which is an empty index.
        }
        return index;
    }

    static byte[] Compress(byte[] bytes) => (_compressor ??= new ZstdSharp.Compressor(Level)).Wrap(bytes).ToArray();

    // Texture data is block-compressed already and gains little from more effort.
    [ThreadStatic] static ZstdSharp.Compressor? _quickCompressor;
    static byte[] CompressTexture(byte[] bytes) => (_quickCompressor ??= new ZstdSharp.Compressor(1)).Wrap(bytes).ToArray();

    static byte[] Unpack(byte[] bytes) => (_decompressor ??= new ZstdSharp.Decompressor()).Unwrap(bytes).ToArray();
}
