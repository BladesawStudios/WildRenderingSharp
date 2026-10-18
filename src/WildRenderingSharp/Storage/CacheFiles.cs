using System.Security.Cryptography;

namespace WildRenderingSharp.Storage;

/// <summary>
/// Reads and packs the big binary files of a prepared model: vertices, indices, material blocks and textures. A packed file is compressed with zstd, and a texture is kept once for
/// the whole cache, however many models use it. Everything that reads one of these files goes through <see cref="ReadAllBytes"/>, which takes either form, so a cache that is part
/// packed, or packed while a host is reading it, works.
/// </summary>
/// <remarks>
/// A file <c>X</c> packed in place becomes <c>X.zst</c>. A texture becomes <c>_textures/&lt;hash&gt;.zst</c> in the cache root, and the model's folder keeps a small
/// <c>textures.idx</c> that says which hash each of its texture files was. JSON and text files stay as they are: they are small, and other tools read them.
/// </remarks>
public static class CacheFiles
{
    /// <summary>The folder of the cache root that holds every texture once.</summary>
    public const string TextureFolder = "_textures";

    const string Suffix = ".zst";
    const string TextureIndex = "textures.idx";

    // Fast to write and faster to read; the cache is read far more often than it is made.
    const int Level = 3;

    [ThreadStatic] static ZstdSharp.Compressor? _compressor;
    [ThreadStatic] static ZstdSharp.Decompressor? _decompressor;

    /// <summary>Whether the file is there, in whichever form.</summary>
    public static bool Exists(string path) => File.Exists(path) || File.Exists(path + Suffix) || SharedTexture(path) is not null;

    /// <summary>The file's bytes, from the plain file, the compressed one beside it, or the cache's shared copy of a texture.</summary>
    public static byte[] ReadAllBytes(string path)
    {
        // The plain file may be deleted between the check and the read by a pack running beside the reader, so each form is tried, not asked about.
        try { return File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }

        try { return Unpack(File.ReadAllBytes(path + Suffix)); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }

        if (SharedTexture(path) is { } shared)
            return Unpack(File.ReadAllBytes(shared));
        throw new FileNotFoundException($"'{path}' is not in the cache, plain, compressed or shared.", path);
    }

    /// <summary>
    /// Packs a model's folder: compresses its vertex, index, shape, context and material-block files in place, and moves its textures into the shared folder. Safe to run again, and
    /// to run while the model is being read.
    /// </summary>
    public static void Pack(string modelDirectory)
    {
        string directory = Path.GetFullPath(modelDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string shared = Path.Combine(Path.GetDirectoryName(directory)!, TextureFolder);

        var textures = Directory.EnumerateFiles(directory, "*.bin").Where(f => !IsGeometryPart(f)).ToList();
        if (textures.Count > 0)
        {
            Directory.CreateDirectory(shared);
            var index = ReadIndex(Path.Combine(directory, TextureIndex));
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
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
        return index;
    }

    static byte[] Compress(byte[] bytes) => (_compressor ??= new ZstdSharp.Compressor(Level)).Wrap(bytes).ToArray();

    static byte[] Unpack(byte[] bytes) => (_decompressor ??= new ZstdSharp.Decompressor()).Unwrap(bytes).ToArray();
}
