namespace WildRenderingSharp.Rom;

/// <summary>Tears of the Kingdom's romfs, with the dictionaries its zstd files need.</summary>
public static class TotkRom
{
    /// <param name="romfsRoot">The base dump's romfs folder.</param>
    /// <param name="modRomfsRoots">Mod romfs folders layered over it, highest priority first; missing folders are dropped.</param>
    public static LayeredRom Open(string romfsRoot, IEnumerable<string>? modRomfsRoots = null)
    {
        var compression = new RomCompression(Path.Combine(romfsRoot, "Pack", "ZsDic.pack.zs"));
        var roots = new List<string> { romfsRoot };
        roots.AddRange((modRomfsRoots ?? []).Reverse());
        return new LayeredRom(roots, compression);
    }
}
