using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using BymlLibrary;
using WildRenderingSharp.Rom;
using WildRenderingSharp.Storage;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>Indexes the per-tile baked lighting in Bake/Scene, mapping each placement hash to the tile its bake is in.</summary>
public static class TotkBake
{
    const string IndexMagic = "WRSB";
    const int IndexVersion = 1;
    const string TileSuffix = ".bkres.zs";

    public const string IndexFile = "index.bin";

    public static void BuildIndex(IRomAccess rom, string outDir, int jobs)
    {
        string[] files = rom.Enumerate("Bake/Scene", "*" + TileSuffix).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        string[] tiles = files.Select(TileName).ToArray();

        var entries = new ConcurrentBag<(ulong Hash, int Tile)>();
        Parallel.For(0, files.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, jobs) }, i =>
        {
            try
            {
                foreach (var model in Models(ReadTile(rom, files[i])))
                    if (ParseGuid(model.GetMap()["Guid"].GetString()) is { } hash)
                        entries.Add((hash, i));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ExportBake] skipped {Path.GetFileName(files[i])}: {ex.Message}");
            }
        });

        var sorted = entries.GroupBy(e => e.Hash).Select(g => g.First()).OrderBy(e => e.Hash).ToArray();
        WriteIndex(outDir, tiles, sorted);
        Console.WriteLine($"[ExportBake] indexed {sorted.Length} placements over {tiles.Length} tiles");
    }

    internal static Byml ReadTile(IRomAccess rom, string path)
    {
        return Byml.FromBinary(rom.ReadSarc(path)["bkdat.byaml"].ToArray());
    }

    internal static string TilePath(string tile) => $"Bake/Scene/{tile}{TileSuffix}";

    internal static ulong? ParseGuid(string guid)
    {
        int cut = guid.IndexOf('_');
        return ulong.TryParse(cut < 0 ? guid : guid[..cut], NumberStyles.None, CultureInfo.InvariantCulture, out ulong hash) ? hash : null;
    }

    internal static IEnumerable<Byml> Models(Byml root)
    {
        foreach (Byml element in root.GetMap()["DataElements"].GetArray())
            if (element.GetMap().TryGetValue("ModelElements", out var models))
                foreach (Byml model in models.GetArray())
                    yield return model;
    }

    static void WriteIndex(string outDir, string[] tiles, (ulong Hash, int Tile)[] sorted)
    {
        AtomicFile.Write(Path.Combine(outDir, IndexFile), temp =>
        {
            using var writer = new BinaryWriter(File.Create(temp), Encoding.UTF8);
            writer.Write(Encoding.ASCII.GetBytes(IndexMagic));
            writer.Write(IndexVersion);
            writer.Write(tiles.Length);
            foreach (string tile in tiles)
                writer.Write(tile);
            writer.Write(sorted.Length);
            foreach (var (hash, tile) in sorted)
            {
                writer.Write(hash);
                writer.Write(tile);
            }
        });
    }

    static string TileName(string path) => Path.GetFileName(path)[..^TileSuffix.Length];
}
