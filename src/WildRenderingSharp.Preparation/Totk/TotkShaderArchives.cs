using WildRenderingSharp.Logging;
using WildRenderingSharp.Rom;
using WildRenderingSharp.Storage;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>The decompressed shader archives, written from the bare ROM into the cache where the shader tools open them by path.</summary>
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
        Log.Info($"[TotkShaderArchives] decompressing '{source}' -> {path}");
        AtomicFile.WriteAllBytes(path, baseRom.ReadAllBytesNested(source));
        return path;
    }
}
