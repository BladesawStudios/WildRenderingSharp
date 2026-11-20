using BfShaderSharp.Pack;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Content.Packs;

/// <summary>The pack of the data that belongs to no model, opened, and the stores a renderer draws from out of it.</summary>
public sealed class SystemPack : IDisposable
{
    public const string ShadersStore = "Shaders", DeferredMaterialsStore = "DeferredMaterials", SystemTexturesStore = "SystemTextures", EffectsStore = "Effects", BakeStore = "Bake", GrassStore = "GrassShaders";

    readonly AssetPack _pack;
    readonly string _stamp;

    public SystemPack(string path, string? programCache)
    {
        _pack = AssetPack.Open(path);
        FileInfo info = new(path);
        _stamp = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        Stores = new ContentStores(Store(ShadersStore), Store(DeferredMaterialsStore), Store(SystemTexturesStore), Store(EffectsStore), programCache);
    }

    public ContentStores Stores { get; }

    /// <summary>The stores of a cache's system pack, the shaders layered so the programs of models can be added to them. The pack stays open for the life of the process, for a tool that holds no owner for it.</summary>
    public static ContentStores StoresOf(CacheLayout cache)
    {
        if (!File.Exists(cache.SystemPack)) throw new FileNotFoundException($"The cache at '{cache.Root}' has no system pack; run the preparer's ensure-system first.", cache.SystemPack);
        ContentStores stores = new SystemPack(cache.SystemPack, Path.Combine(cache.Root, "_glprograms")).Stores;
        return stores with { Shaders = new LayeredContentFiles(stores.Shaders) };
    }

    public PackContentFiles Store(string name) => new(_pack, name, _stamp);

    public void Dispose() => _pack.Dispose();
}
