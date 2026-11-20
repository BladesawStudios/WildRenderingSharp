using System.Collections.Concurrent;
using KingRenderingSharp.Content.Assets.Sources;
using KingRenderingSharp.Content.Packs;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Hosting.Content;

/// <summary>What a renderer draws from: the system pack and the packs of the models, read directly. </summary>
internal sealed class KingContent : IDisposable
{
    readonly CacheLayout _cache;
    readonly ConcurrentDictionary<string, Lazy<ModelPack>> _packs = new(StringComparer.Ordinal);
    readonly SystemPack _system;
    readonly LayeredContentFiles _shaders;

    public KingContent(CacheLayout cache)
    {
        _cache = cache;
        _system = new SystemPack(cache.SystemPack, Path.Combine(cache.Root, "_glprograms"));
        // The programs of the models are in their own packs' archives, and are added to the shaders as the packs are opened.
        _shaders = new LayeredContentFiles(_system.Stores.Shaders);
        Stores = _system.Stores with { Shaders = _shaders };
    }

    public ContentStores Stores { get; }

    public IModelSource OpenModel(string resolvedModelName)
    {
        if (!File.Exists(_cache.ModelPack(resolvedModelName))) throw new FileNotFoundException($"The cache has no pack for the model '{resolvedModelName}'.", _cache.ModelPack(resolvedModelName));
        ModelPack pack = _packs.GetOrAdd(resolvedModelName, name => new Lazy<ModelPack>(() => new ModelPack(_cache.ModelPack(name), _cache.ModelShaders(name), name))).Value;
        _shaders.Add(pack.Programs);
        return pack.Source;
    }

    public void Dispose()
    {
        foreach (Lazy<ModelPack> pack in _packs.Values) if (pack.IsValueCreated) pack.Value.Dispose();
        _system.Dispose();
    }
}
