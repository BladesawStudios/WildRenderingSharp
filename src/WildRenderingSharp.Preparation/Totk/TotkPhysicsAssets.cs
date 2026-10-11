using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>Copies an actor's cloth and helper-bone physics files out of its pack into its cache directory.</summary>
public static class TotkPhysicsAssets
{
    static readonly string[] Extensions = [".bphcl", ".bphhb"];

    public static void Extract(IRomAccess rom, string actorOrModelName, string modelName, string dataDirectory, Action<string>? log)
    {
        if (!ExtractFromPacks(rom, [actorOrModelName, modelName], dataDirectory, log))
            CopyTestAssets(actorOrModelName, modelName, dataDirectory, log);
    }

    // Through the mods, so a mod's own cloth and helper-bone data comes with its actor pack.
    static bool ExtractFromPacks(IRomAccess rom, string[] names, string dataDirectory, Action<string>? log)
    {
        foreach (string name in names)
        {
            string packPath = $"Pack/Actor/{name}.pack.zs";
            if (!rom.Exists(packPath))
                continue;

            try
            {
                var sarc = rom.ReadSarc(packPath);
                bool found = false;
                foreach (var (entry, data) in sarc.Where(e => HasPhysicsExtension(e.Key)))
                {
                    string outPath = Path.Combine(dataDirectory, Path.GetFileName(entry));
                    File.WriteAllBytes(outPath, data.ToArray());
                    log?.Invoke($"[prepare] extracted physics asset '{Path.GetFileName(entry)}' -> {outPath}");
                    found = true;
                }
                if (found)
                    return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"[prepare] error extracting physics from '{packPath}': {ex.Message}");
            }
        }
        return false;
    }

    // Developer checkouts keep stand-in cloth assets beside the build, for actors whose romfs pack has none.
    static void CopyTestAssets(string actorOrModelName, string modelName, string dataDirectory, Action<string>? log)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] roots =
        [
            Path.Combine(baseDir, "res", "test_cloth"),
            Path.Combine(baseDir, "..", "..", "..", "..", "res", "test_cloth"),
            Path.Combine(Directory.GetCurrentDirectory(), "res", "test_cloth"),
        ];

        foreach (string root in roots.Where(Directory.Exists))
        {
            bool found = false;
            foreach (string directory in new[] { actorOrModelName, modelName }.Select(n => Path.Combine(root, n)).Where(Directory.Exists))
            {
                foreach (string file in Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories).Where(HasPhysicsExtension))
                {
                    string outPath = Path.Combine(dataDirectory, Path.GetFileName(file));
                    File.Copy(file, outPath, overwrite: true);
                    log?.Invoke($"[prepare] copied test physics asset '{Path.GetFileName(file)}' -> {outPath}");
                    found = true;
                }
            }
            if (found)
                return;
        }
    }

    static bool HasPhysicsExtension(string name) => Extensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
}
