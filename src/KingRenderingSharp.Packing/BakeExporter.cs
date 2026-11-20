using BfShaderSharp.Assets;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Packing;

/// <summary>Imports the baked lighting into packs the renderer's bake library reads: <c>index.bfgd</c> holds the index of every placement, and each run adds a pack of the tiles it was asked for with their atlas textures.</summary>
/// <remarks>Answers in the preparer protocol, a KRS_BEGIN line for each tile and then KRS_DONE or KRS_FAIL. A tile some pack in the folder already holds is not imported again unless forced.</remarks>
internal static class BakeExporter
{
    const string TilePrefix = "Bake/tile/";

    public static int Run(string romfs, CacheLayout layout, IReadOnlyCollection<string> tiles, int jobs, bool force, Action<string> say)
    {
        Directory.CreateDirectory(layout.Bake);
        using RomfsFiles files = new(romfs);
        string index = Path.Combine(layout.Bake, "index.bfgd");
        if (force || !File.Exists(index))
            WritePack(index, files, context => BakeImporter.Import(context, [], jobs > 0 ? jobs : Environment.ProcessorCount, true));

        HashSet<string> have = force ? [] : HeldTiles(layout.Bake);
        string[] wanted = [.. tiles.Where(t => !have.Contains(t))];
        foreach (string tile in tiles.Where(have.Contains)) say($"KRS_DONE {tile}\t{tile}");
        if (wanted.Length == 0) return 0;

        string pack = Path.Combine(layout.Bake, $"tiles-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Environment.ProcessId}.bfgd");
        List<string> done = [];
        WritePack(pack, files, context =>
        {
            Parallel.ForEach(wanted, new ParallelOptions { MaxDegreeOfParallelism = jobs > 0 ? jobs : Math.Max(1, Environment.ProcessorCount / 2) }, tile =>
            {
                say($"KRS_BEGIN {tile}");
                try
                {
                    BakeImporter.Import(context, [tile], 1, false);
                    lock (done) done.Add(tile);
                }
                catch (Exception exception)
                {
                    say($"KRS_FAIL {tile}\t{exception.GetBaseException().Message.ReplaceLineEndings(" ")}");
                }
            });
        });

        // The pack is reported only once it is in place, so a host that reads on a tile's report finds it.
        foreach (string tile in done) say($"KRS_DONE {tile}\t{tile}");
        return 0;
    }

    static void WritePack(string path, RomfsFiles files, Action<AssetContext> import)
    {
        string temporary = path + ".tmp";
        using (FileStream output = File.Create(temporary))
        {
            PackWriter writer = new(output);
            import(new AssetContext(writer, files, ShaderArchiveSet.Empty, new Dictionary<ulong, string>(), TextureStorage.Linear));
            writer.Finish();
        }

        File.Move(temporary, path, true);
    }

    static HashSet<string> HeldTiles(string folder)
    {
        HashSet<string> held = new(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(folder, "*.bfgd"))
        {
            using AssetPack pack = AssetPack.Open(file);
            foreach (PackEntry entry in pack.Of(PackEntryKind.GameData).Where(e => e.Name.StartsWith(TilePrefix, StringComparison.Ordinal))) held.Add(entry.Name[TilePrefix.Length..]);
        }

        return held;
    }
}
