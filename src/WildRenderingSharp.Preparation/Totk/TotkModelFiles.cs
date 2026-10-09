using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>
/// Finds a model in <c>Model/&lt;pack&gt;.&lt;model&gt;.bfres.mc</c> from any of the three names a person would type: the full stem,
/// a short name whose pack and model coincide (<c>Weapon_Sword_070</c>), or just the model half when only one file ends with it.
/// </summary>
public static class TotkModelFiles
{
    const string Suffix = ".bfres.mc";

    /// <summary>The game-relative path of the model's file, or null when no layer has it or the name is ambiguous.</summary>
    public static string? Find(IRomAccess rom, string name)
    {
        foreach (string stem in new[] { name, $"{name}.{name}" })
        {
            string path = $"Model/{stem}{Suffix}";
            if (rom.Exists(path))
                return path;
        }

        var hits = rom.Enumerate("Model", $"*.{name}{Suffix}").ToArray();
        return hits.Length == 1 ? hits[0] : null;
    }

    /// <summary>Why <see cref="Find"/> found nothing, with near matches: a wrong name is the usual way to fail here.</summary>
    public static string Explain(IRomAccess rom, string name)
    {
        var ambiguous = rom.Enumerate("Model", $"*.{name}{Suffix}").ToArray();
        if (ambiguous.Length > 1)
            return $"\"{name}\" is ambiguous - it matches {ambiguous.Length} files. Use the full stem, e.g.\n"
                + string.Join("\n", ambiguous.Take(6).Select(f => "    " + Stem(f)));

        string needle = name.Contains('.') ? name.Split('.').Last() : name;
        var near = rom.Enumerate("Model", "*" + Suffix).Select(Stem)
            .Where(stem => stem.Contains(needle, StringComparison.OrdinalIgnoreCase)).Take(8).ToList();
        if (near.Count > 0)
            return $"no model \"{name}\". Did you mean one of:\n" + string.Join("\n", near.Select(stem => "    " + stem));

        return $"no model \"{name}\" under Model/.\n    Model files are <pack>.<model>.bfres.mc - pass either the full stem or, when unique, just the model half.";
    }

    static string Stem(string path) => Path.GetFileName(path)[..^Suffix.Length];
}
