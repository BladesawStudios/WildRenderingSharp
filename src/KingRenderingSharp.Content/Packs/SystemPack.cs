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

    public PackContentFiles Store(string name) => new(_pack, name, _stamp);

    public void Dispose() => _pack.Dispose();
}
