using BymlLibrary;
using SarcLibrary;
using WildRenderingSharp.Logging;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>Resolves an actor to its model file stem and animation archives from its pack's ModelInfo and AnimationParam.</summary>
public static class TotkActorInfo
{
    public sealed record Resolved(string ModelName, IReadOnlyList<string> AnimPackNames);

    public static Resolved? Resolve(IRomAccess rom, string actorName)
    {
        string packPath = $"Pack/Actor/{actorName}.pack.zs";
        if (!rom.Exists(packPath))
            return null;

        var sarc = rom.ReadSarc(packPath);
        var entries = sarc.Select(kv => kv.Key).ToList();
        if (ModelInfoKey(sarc, entries, actorName) is not { } modelInfoKey)
        {
            Log.Warning($"[ActorInfo] '{actorName}' has a pack but no Component/ModelInfo, so no model can be resolved from it.");
            return null;
        }

        // Some "Player*" effect actors carry a ModelInfo of another schema that names no model.
        Byml? project = null, fmdb = null;
        foreach (var file in ParentChain(sarc, entries.ToHashSet(StringComparer.Ordinal), modelInfoKey))
        {
            if (project == null && file.TryGetValue("ModelProjectName", out var p)) project = p;
            if (fmdb == null && file.TryGetValue("FmdbName", out var f)) fmdb = f;
        }
        if (project == null || fmdb == null)
        {
            Log.Info($"[ActorInfo] '{actorName}' has a Component/ModelInfo but no ModelProjectName/FmdbName in it - not a real model.");
            return null;
        }

        string modelName = $"{project.GetString()}.{fmdb.GetString()}";
        var animPacks = AnimPackNames(sarc, entries);
        Log.Info($"[ActorInfo] '{actorName}' -> model '{modelName}', anim archives: [{string.Join(", ", animPacks)}]");
        return new Resolved(modelName, animPacks);
    }

    // A pack can hold several ModelInfo files (an armour piece's leggings pack carries the helmet's too), so the one to read is the
    // one the actor's ActorParam points at, possibly inherited; failing that, the file named for the actor, then the first.
    static string? ModelInfoKey(Sarc sarc, List<string> entries, string actorName)
    {
        var names = entries.ToHashSet(StringComparer.Ordinal);
        foreach (var actorParam in ParentChain(sarc, names, $"Actor/{actorName}.engine__actor__ActorParam.bgyml"))
        {
            if (actorParam.TryGetValue("Components", out var components) && components.Type == BymlNodeType.Map
                && components.GetMap().TryGetValue("ModelInfoRef", out var reference) && reference.Type == BymlNodeType.String
                && reference.GetString().Length > 0)
            {
                string key = EntryName(reference.GetString());
                if (names.Contains(key))
                    return key;
                break;
            }
        }
        string named = $"Component/ModelInfo/{actorName}.engine__component__ModelInfo.bgyml";
        return names.Contains(named) ? named : entries.FirstOrDefault(k => k.StartsWith("Component/ModelInfo/", StringComparison.Ordinal));
    }

    static List<string> AnimPackNames(Sarc sarc, List<string> entries)
    {
        var packs = new List<string>();
        string? key = entries.FirstOrDefault(k => k.StartsWith("Component/AnimationParam/", StringComparison.Ordinal));
        if (key == null)
            return packs;
        var animParam = Byml.FromBinary(sarc[key].ToArray()).GetMap();
        if (animParam.TryGetValue("AnimationResources", out var resources))
            foreach (Byml entry in resources.GetArray())
                if (entry.GetMap().TryGetValue("ModelProjectName", out var name))
                    packs.Add(name.GetString());
        return packs;
    }

    static List<IDictionary<string, Byml>> ParentChain(Sarc sarc, HashSet<string> entries, string name)
    {
        var chain = new List<IDictionary<string, Byml>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? at = name;
        while (at != null && seen.Add(at) && entries.Contains(at))
        {
            IDictionary<string, Byml> file;
            try { file = Byml.FromBinary(sarc[at].ToArray()).GetMap(); }
            catch { break; }
            chain.Add(file);
            at = file.TryGetValue("$parent", out var parent) && parent.Type == BymlNodeType.String && parent.GetString().Length > 0
                ? EntryName(parent.GetString()) : null;
        }
        return chain;
    }

    static string EntryName(string reference)
    {
        string name = reference.TrimStart('?');
        if (name.StartsWith("Work/", StringComparison.Ordinal))
            name = name["Work/".Length..];
        return name.EndsWith(".gyml", StringComparison.Ordinal) ? name[..^".gyml".Length] + ".bgyml" : name;
    }
}
