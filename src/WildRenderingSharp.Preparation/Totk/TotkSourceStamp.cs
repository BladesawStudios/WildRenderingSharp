using System.Text.Json;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>
/// Which romfs file supplied each source a prepared model was built from, so a model is prepared again when toggling a mod changes which
/// file would win, or a winning file changes.
/// </summary>
public static class TotkSourceStamp
{
    public const string FileName = "romfs_sources.json";

    // Bumped when preparation starts producing something an earlier model could be missing, so IsUpToDate sends it through
    // again. 2: the model's own <Project>.anim.bfres joins the anim archives. 3: every level of detail is exported. 4: .bntx
    // textures export (CmnTex_BakeDefault). 5: textures export their whole mip chain.
    const int PreparationVersion = 5;

    sealed record Entry(string? Path, long Size, long MTime);

    sealed record Stamp(string Romfs, List<string> Mods, Dictionary<string, Entry> Files, int Version = 0);

    public static void Delete(string dataDirectory) => File.Delete(Path.Combine(dataDirectory, FileName));

    public static void Write(string dataDirectory, TotkRomfs romfs, IReadOnlyDictionary<string, string?> winners, Action<string>? log)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, winner) in winners)
        {
            var info = winner is null ? null : new FileInfo(winner);
            entries[path] = info is { Exists: true } ? new Entry(winner, info.Length, info.LastWriteTimeUtc.Ticks) : new Entry(null, 0, 0);
        }

        foreach (var (path, entry) in entries.Where(e => e.Value.Path is { } p && romfs.ModRoots.Any(m => p.StartsWith(m, StringComparison.OrdinalIgnoreCase))))
            log?.Invoke($"[prepare] mod file: {path} <- {entry.Path}");

        var stamp = new Stamp(romfs.Root, romfs.ModRoots.ToList(), entries, PreparationVersion);
        File.WriteAllText(Path.Combine(dataDirectory, FileName), JsonSerializer.Serialize(stamp, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool IsUpToDate(TotkRomfs romfs, string dataDirectory)
    {
        string stampPath = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(stampPath))
            return romfs.ModRoots.Count == 0;

        Stamp? stamp;
        try { stamp = JsonSerializer.Deserialize<Stamp>(File.ReadAllText(stampPath)); }
        catch { return false; }
        if (stamp is null || stamp.Version != PreparationVersion)
            return false;

        foreach (var (path, recorded) in stamp.Files)
        {
            string? now = romfs.Layered.Locate(path);
            if (!SamePath(now, recorded.Path))
                return false;
            if (now is not null && !SameFile(new FileInfo(now), recorded))
                return false;
        }
        return true;
    }

    static bool SamePath(string? a, string? b) =>
        string.Equals(a is null ? null : Path.GetFullPath(a), b is null ? null : Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    static bool SameFile(FileInfo info, Entry recorded) => info.Length == recorded.Size && info.LastWriteTimeUtc.Ticks == recorded.MTime;
}
