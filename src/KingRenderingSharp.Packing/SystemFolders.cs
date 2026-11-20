using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Packing;

/// <summary>The folders the system data is written into while it is made, before <see cref="SystemSealer"/> folds them into the system pack and removes them: the stores of the pack, one folder each.</summary>
sealed class SystemFolders(CacheLayout cache)
{
    string Folder(string name) => Path.Combine(cache.Root, "_staging", name);

    public string Shaders => Folder("Shaders");

    public string DeferredMaterials => Folder("DeferredMaterials");

    public string SystemTextures => Folder("SystemTextures");

    public string Effects => Folder("Effects");

    public string GrassShaders => Folder("GrassShaders");

    public string SystemPack => cache.SystemPack;

    /// <summary>Removes what is left of the folders.</summary>
    public void Clear()
    {
        string root = Path.Combine(cache.Root, "_staging");
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
