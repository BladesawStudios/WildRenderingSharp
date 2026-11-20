using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Packing;

/// <summary>Builds the system pack of a cache from a romfs unless it is there: the data of the game that belongs to no model, written into folders by <see cref="SystemExporter"/> and folded into the pack by <see cref="SystemSealer"/>.</summary>
static class EnsureSystem
{
    public static int Run(string romfs, CacheLayout layout, Action<string> log)
    {
        if (File.Exists(layout.SystemPack)) return 0;
        // Preparer processes start together, so the one that gets here first makes the pack and the others wait and find it there.
        using CacheLock gate = CacheLock.Acquire(layout, "system-assets");
        if (File.Exists(layout.SystemPack)) return 0;
        SystemFolders folders = new(layout);
        folders.Clear();
        int exit = SystemExporter.Export(romfs, folders, log);
        int result = exit != 0 ? exit : SystemSealer.Seal(folders, log);
        folders.Clear();
        return result;
    }
}
