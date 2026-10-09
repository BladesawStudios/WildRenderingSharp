using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using BymlLibrary;
using SarcLibrary;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>
/// The game's baked lighting for placed static actors: <c>Bake/Scene/*.bkres.zs</c>, a SARC per bake tile of a map
/// (<c>MainField_G1_17_7</c>, about 10,300 of them) holding one <c>bkdat.byaml</c>. It names the tile's atlas texture and, for every
/// placed actor in the tile, each material's region of that atlas. Which tile a placement's bake is in is not derivable from where it
/// stands, so <see cref="BuildIndex"/> reads every tile once and records hash to tile, and <see cref="TotkBakeTile.Export"/> then
/// exports one tile's atlas (its whole mip chain) and table.
/// </summary>
public static class TotkBake
{
    const string IndexMagic = "WRSB";
    const int IndexVersion = 1;
    const string TileSuffix = ".bkres.zs";

    /// <summary>The index file, under the bake directory.</summary>
    public const string IndexFile = "index.bin";

    /// <summary>
    /// Reads every <c>Bake/Scene</c> tile and writes <c>&lt;outDir&gt;/index.bin</c>: the tile names, then every placement hash sorted
    /// with the tile its bake is in. Binary rather than JSON: it is several hundred thousand entries.
    /// </summary>
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
        var sarc = Sarc.FromBinary(new ArraySegment<byte>(rom.ReadAllBytesNested(path).ToArray()));
        return Byml.FromBinary(sarc["bkdat.byaml"].ToArray());
    }

    internal static string TilePath(string tile) => $"Bake/Scene/{tile}{TileSuffix}";

    /// <summary>"10033483703293899051_0" -&gt; the placement hash.</summary>
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
        Directory.CreateDirectory(outDir);
        string temp = Path.Combine(outDir, IndexFile + ".tmp");
        using (var writer = new BinaryWriter(File.Create(temp), Encoding.UTF8))
        {
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
        }
        File.Move(temp, Path.Combine(outDir, IndexFile), overwrite: true);
    }

    static string TileName(string path) => Path.GetFileName(path)[..^TileSuffix.Length];
}
