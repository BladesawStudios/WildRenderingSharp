using System.Text.Json;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>Records which romfs file supplied each source of a prepared model, so it is prepared again when a mod toggle or file change alters what wins.</summary>
/// <remarks>
/// A model's preparation is made of parts that each have a version: Geometry (vertex, index, texture, skeleton and animation files, and everything after it when stale),
/// Material (the material uniform blocks, which makes the manifest stale) and Manifest (the manifest and the shader programs it names). Packing the files (CacheFiles.Pack)
/// is a fourth part, and the stamp notes whether animations were imported. A stamp that carries a single Version applies it to every part.
/// </remarks>
public static class TotkSourceStamp
{
    public const string FileName = "romfs_sources.json";

    // Bump the version of the part whose output changed.
    const int GeometryVersion = 7, MaterialVersion = 6, ManifestVersion = 7;

    const int PackVersion = 1;

    sealed record Entry(string? Path, long Size, long MTime);

    sealed record Stamp(string Romfs, List<string> Mods, Dictionary<string, Entry> Files, int Version = 0, int Geometry = 0, int Material = 0, int Manifest = 0, int Packed = 0, bool? Animations = null);

    // The parts of a model's preparation that have to be done again.
    [Flags]
    public enum Stages { None = 0, Manifest = 1, Material = 2, Geometry = 4, Pack = 8, All = Manifest | Material | Geometry | Pack }

    public static void Delete(string dataDirectory) => File.Delete(Path.Combine(dataDirectory, FileName));

    public static void Write(string dataDirectory, TotkRomfs romfs, IReadOnlyDictionary<string, string?> winners, Action<string>? log, bool keepEarlier = false, bool? animations = null)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        bool? keptAnimations = null;
        // A part redone alone reads only some of the sources, so the others stay as they were recorded.
        if (keepEarlier && Read(dataDirectory) is { } earlier)
        {
            foreach (var (path, entry) in earlier.Files)
                entries[path] = entry;
            keptAnimations = earlier.Animations;
        }
        foreach (var (path, winner) in winners)
        {
            var info = winner is null ? null : new FileInfo(winner);
            entries[path] = info is { Exists: true } ? new Entry(winner, info.Length, info.LastWriteTimeUtc.Ticks) : new Entry(null, 0, 0);
        }

        foreach (var (path, entry) in entries.Where(e => e.Value.Path is { } p && romfs.ModRoots.Any(m => p.StartsWith(m, StringComparison.OrdinalIgnoreCase))))
            log?.Invoke($"[prepare] mod file: {path} <- {entry.Path}");

        var stamp = new Stamp(romfs.Root, romfs.ModRoots.ToList(), entries, 0, GeometryVersion, MaterialVersion, ManifestVersion, PackVersion, animations ?? keptAnimations);
        File.WriteAllText(Path.Combine(dataDirectory, FileName), JsonSerializer.Serialize(stamp, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool IsUpToDate(TotkRomfs romfs, string dataDirectory) => Stale(romfs, dataDirectory, animations: false) == Stages.None;

    static Stamp? Read(string dataDirectory)
    {
        string stampPath = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(stampPath))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Stamp>(File.ReadAllText(stampPath));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A stamp that cannot be read counts as no stamp, so the model is prepared again.
            return null;
        }
    }

    // Which parts of the model's preparation are out of date: all of them when a source file changed or there is no usable stamp, otherwise those whose version is older.
    public static Stages Stale(TotkRomfs romfs, string dataDirectory, bool animations)
    {
        if (!File.Exists(Path.Combine(dataDirectory, FileName)))
            return romfs.ModRoots.Count == 0 ? Stages.None : Stages.All;
        if (Read(dataDirectory) is not { } stamp)
            return Stages.All;

        // A stamp with one Version for every part gives each part that number.
        int geometry = stamp.Geometry != 0 ? stamp.Geometry : stamp.Version;
        int material = stamp.Material != 0 ? stamp.Material : stamp.Version;
        int manifest = stamp.Manifest != 0 ? stamp.Manifest : stamp.Version;

        foreach (var (path, recorded) in stamp.Files)
        {
            string? now = romfs.Layered.Locate(path);
            if (!SamePath(now, recorded.Path))
                return Stages.All;
            if (now is not null && !SameFile(new FileInfo(now), recorded))
                return Stages.All;
        }

        if (geometry != GeometryVersion)
            return Stages.All;

        // A model prepared without its animations is prepared again when they are asked for.
        if (animations && stamp.Animations == false)
            return Stages.All;
        if (material != MaterialVersion)
            return Stages.Material | Stages.Manifest;
        var stale = manifest != ManifestVersion ? Stages.Manifest : Stages.None;
        return stamp.Packed != PackVersion ? stale | Stages.Pack : stale;
    }

    // Records that the model's files have been packed, leaving the rest of its stamp as it was.
    public static void MarkPacked(string dataDirectory)
    {
        if (Read(dataDirectory) is not { } stamp)
            return;
        File.WriteAllText(Path.Combine(dataDirectory, FileName), JsonSerializer.Serialize(stamp with { Packed = PackVersion }, new JsonSerializerOptions { WriteIndented = true }));
    }

    static bool SamePath(string? a, string? b) =>
        string.Equals(a is null ? null : Path.GetFullPath(a), b is null ? null : Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    static bool SameFile(FileInfo info, Entry recorded) => info.Length == recorded.Size && info.LastWriteTimeUtc.Ticks == recorded.MTime;
}
