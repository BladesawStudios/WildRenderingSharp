namespace WildRenderingSharp.Rom.Games;

/// <summary>Tears of the Kingdom's romfs with its zstd dictionaries, and any mod folders layered over it.</summary>
public static class TotkRom
{
    public static LayeredRom Open(string romfsRoot, IEnumerable<string>? modRomfsRoots = null)
    {
        var compression = new RomCompression(Path.Combine(romfsRoot, "Pack", "ZsDic.pack.zs"));
        var roots = new List<string> { romfsRoot };
        roots.AddRange((modRomfsRoots ?? []).Reverse());
        return new LayeredRom(roots, compression);
    }
}
