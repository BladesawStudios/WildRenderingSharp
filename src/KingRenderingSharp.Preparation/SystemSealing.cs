using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Preparation;

/// <summary>Has the pack tool fold the system data this preparer wrote into folders into the pack the renderer reads. Without the tool the folders stay, and the renderer reads them.</summary>
static class SystemSealing
{
    public static void Seal(CacheLayout cache, Action<string>? log, string? romfs = null)
    {
        if (File.Exists(cache.SystemPack) || PackTool.Executable() is null) return;
        // The data the pack tool imports itself is written into the folders first, over what this preparer wrote.
        if (romfs is not null && PackTool.Run(["system", "--romfs", romfs, "--cache", cache.Root], line => log?.Invoke(line)) != 0) log?.Invoke("[prepare] the pack tool's system import failed; the older files stay");
        int exit = PackTool.Run(["seal", "--cache", cache.Root], line => log?.Invoke(line));
        if (exit != 0) log?.Invoke($"[prepare] sealing the system data failed (exit code {exit}); the renderer reads the folders");
    }
}
