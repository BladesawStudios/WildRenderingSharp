using System.Globalization;

namespace WildRenderingSharp.Hosting.Preparers;

/// <summary>The command lines the preparer process is given and the progress lines it answers with.</summary>
static class PreparerProtocol
{
    public const string ResultPrefix = "WRS_RESULT ";
    public const string BeginPrefix = "WRS_BEGIN ";
    public const string DonePrefix = "WRS_DONE ";
    public const string FailPrefix = "WRS_FAIL ";

    public static List<string> EnsureSystem(string romfsRoot, string cacheRoot) => ["ensure-system", "--romfs", romfsRoot, "--cache", cacheRoot];

    public static List<string> Prepare(PrepareRequest request)
    {
        List<string> args = ["prepare", "--romfs", request.RomfsRoot, "--actor", request.ActorOrModelName, "--cache", request.Cache.Root];
        AddShared(args, request.ModRomfsLayers, request.ImportAnimations, request.Force);
        return args;
    }

    public static List<string> PrepareBatch(PrepareBatchRequest request, string listFile, int jobs)
    {
        List<string> args = ["prepare-batch", "--romfs", request.RomfsRoot, "--cache", request.Cache.Root,
            "--list", listFile, "--jobs", jobs.ToString(CultureInfo.InvariantCulture)];
        AddShared(args, request.ModRomfsLayers, request.ImportAnimations, request.Force);
        return args;
    }

    public static List<string> PrepareBake(string romfsRoot, string cacheRoot, string listFile) =>
        ["prepare-bake", "--romfs", romfsRoot, "--cache", cacheRoot, "--list", listFile];

    // A line that starts with the prefix and carries a tab-separated name and value.
    public static bool TrySplit(string line, string prefix, out string name, out string value)
    {
        name = value = "";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        string rest = line[prefix.Length..];
        int tab = rest.IndexOf('\t');
        if (tab < 0)
            return false;
        name = rest[..tab];
        value = rest[(tab + 1)..];
        return true;
    }

    static void AddShared(List<string> args, IReadOnlyList<string>? mods, bool importAnimations, bool force)
    {
        foreach (string mod in mods ?? [])
        {
            args.Add("--mod");
            args.Add(mod);
        }
        if (!importAnimations)
            args.Add("--no-anims");
        if (force)
            args.Add("--force");
    }
}
