using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>
/// The decompressed <c>Shader/&lt;name&gt;.Product.110.product.Nin_NX_NVN.bfsha</c> archives, written into the cache where the shader
/// tools open them by path. They come from the bare ROM, since the decompiled programs are keyed on the shipped archives.
/// </summary>
public static class TotkShaderArchives
{
    const string Suffix = ".Product.110.product.Nin_NX_NVN.bfsha";

    public static string Folder(CacheLayout cache) => Path.Combine(cache.Root, "_totk_shader_archives");

    public static string Extract(IRomAccess baseRom, CacheLayout cache, string name)
    {
        string path = Path.Combine(Folder(cache), name + Suffix);
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            return path;

        string plain = $"Shader/{name}{Suffix}";
        string source = baseRom.Exists(plain) ? plain : plain + ".zs";
        Console.WriteLine($"[TotkShaderArchives] decompressing '{source}' -> {path}");
        Write(path, baseRom.ReadAllBytesNested(source));
        return path;
    }

    /// <summary>Writes through a temp file, so an interrupted run cannot leave a truncated archive that later runs would reuse.</summary>
    public static void Write(string path, ReadOnlySpan<byte> bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temp, bytes.ToArray());
        File.Move(temp, path, overwrite: true);
    }
}
