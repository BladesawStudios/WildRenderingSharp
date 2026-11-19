using System.Collections.Concurrent;
using KingRenderingSharp.Content.Assets.Sources;
using KingRenderingSharp.Content.Packs;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Hosting.Content;

/// <summary>What a renderer draws from: the system pack and the packs of the models, read directly. A model with no pack is read from its folder, as a game whose models are not packed has them.</summary>
internal sealed class KingContent : IDisposable
{
    readonly CacheLayout _cache;
    readonly ConcurrentDictionary<string, Lazy<ModelPack>> _packs = new(StringComparer.Ordinal);
    readonly SystemPack? _system;
    readonly LayeredContentFiles _shaders;

    public KingContent(CacheLayout cache)
    {
        _cache = cache;
        _system = File.Exists(cache.SystemPack) ? new SystemPack(cache.SystemPack, Path.Combine(cache.Root, "_glprograms")) : null;
        ContentStores stores = _system?.Stores ?? ContentStores.FromCache(cache);
        // The programs of the models are in their own packs' archives, and are added to the shaders as the packs are opened.
        _shaders = new LayeredContentFiles(stores.Shaders);
        // Programs a game whose models are not packed has prepared since the system data was sealed are in the folder.
        if (_system is not null) _shaders.Add(new FolderContentFiles(cache.Shaders));
        Stores = stores with { Shaders = _shaders };
    }

    public ContentStores Stores { get; }

    public IModelSource OpenModel(string resolvedModelName)
    {
        if (!File.Exists(_cache.ModelPack(resolvedModelName))) return new DirectoryModelSource(_cache.ModelDirectory(resolvedModelName));
        ModelPack pack = _packs.GetOrAdd(resolvedModelName, name => new Lazy<ModelPack>(() => new ModelPack(_cache.ModelPack(name), _cache.ModelShaders(name), name))).Value;
        _shaders.Add(pack.Programs);
        return pack.Source;
    }

    public void Dispose()
    {
        foreach (Lazy<ModelPack> pack in _packs.Values) if (pack.IsValueCreated) pack.Value.Dispose();
        _system?.Dispose();
    }
}
