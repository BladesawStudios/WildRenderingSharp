using BfShaderSharp.Assets;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Packing;

/// <summary>Runs the pack preparer for one model or a list of them and reports in the preparer protocol.</summary>
sealed class PackRun(string romfs, CacheLayout cache, bool animations, bool force, IReadOnlyList<string>? layers = null)
{
    const string ResultPrefix = "KRS_RESULT ";
    readonly object _output = new();

    void Say(string line)
    {
        lock (_output)
        {
            Console.WriteLine(line);
            Console.Out.Flush();
        }
    }

    /// <summary>Prepares one model, unless a manifest for the name is already in the cache and <c>force</c> is not set; a model file under a name of its own (one made from its content) is thereby prepared once.</summary>
    public int One(string name, IReadOnlyList<string> animationPacks, string? modelFile = null)
    {
        try
        {
            string model = Prepare(name, animationPacks, Say, modelFile: modelFile);
            Say(ResultPrefix + model);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    /// <summary>Prepares every distinct name of the lines; a line starting with + also asks for that model's animations.</summary>
    public int Batch(IEnumerable<string> lines, int jobs)
    {
        List<(string Name, bool Animated, string[] Packs)> names = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string raw in lines.Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            bool animated = raw.StartsWith('+');
            string text = animated ? raw[1..].Trim() : raw;
            // A line is a model name, then optionally | and the comma-separated animation packs the actor names.
            string[] parts = text.Split('|', 2);
            string name = parts[0].Trim();
            if (seen.Add(name)) names.Add((name, animated, parts.Length > 1 ? parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : []));
        }

        Parallel.ForEach(names, new ParallelOptions { MaxDegreeOfParallelism = jobs > 0 ? jobs : Math.Max(1, Environment.ProcessorCount / 2) }, entry =>
        {
            Say("KRS_BEGIN " + entry.Name);
            try
            {
                string model = Prepare(entry.Name, entry.Packs, null, entry.Animated);
                Say($"KRS_DONE {entry.Name}\t{model}");
            }
            catch (Exception exception)
            {
                Say($"KRS_FAIL {entry.Name}\t{exception.GetBaseException().Message.ReplaceLineEndings(" ")}");
            }
        });
        return 0;
    }

    string Prepare(string name, IReadOnlyList<string> animationPacks, Action<string>? log, bool animatedAnyway = false, string? modelFile = null)
    {
        string? actorName = null;
        // A name that is an actor's resolves to its model, with the animation archives its pack lists unless the caller named some; a model name resolves to itself.
        using (RomfsFiles files = new(romfs, layers))
        {
            ActorImporter.Resolved? actor = modelFile is null ? ActorImporter.Resolve(files, name) : null;
            if (actor is not null)
            {
                log?.Invoke($"[prepare] resolved actor '{name}' -> model '{actor.ModelName}', anim archives: {(actor.AnimPackNames.Count == 0 ? "(none named)" : string.Join(", ", actor.AnimPackNames))}");
                if (animationPacks.Count == 0) animationPacks = actor.AnimPackNames;
                actorName = name;
                name = actor.ModelName;
            }
        }

        if (!force && cache.IsPrepared(name) && File.Exists(StampFile(name)) == (Stamp.Length > 0) && (Stamp.Length == 0 || File.ReadAllText(StampFile(name)) == Stamp))
        {
            log?.Invoke($"[prepare] {name} is already prepared.");
            return name;
        }

        PreparedPackModel prepared = PackPreparer.Prepare(romfs, name, cache.ModelPack(name), cache.ModelShaders(name), log, animations || animatedAnyway, animationPacks, modelFile, layers);
        WriteStamp(name);
        foreach (string problem in prepared.Problems) log?.Invoke("[prepare] " + problem);
        using (RomfsFiles files = new(romfs, layers))
            foreach ((string file, byte[] data) in ActorImporter.Physics(files, actorName ?? name, name))
            {
                Directory.CreateDirectory(cache.ModelDirectory(name));
                File.WriteAllBytes(Path.Combine(cache.ModelDirectory(name), file), data);
                log?.Invoke($"[prepare] extracted physics asset '{file}'");
            }

        log?.Invoke($"[prepare] {prepared.Name}: {prepared.Shapes} shapes, {prepared.Programs} programs, {prepared.Textures} textures, {prepared.Animations} animations");
        return prepared.Name;
    }

    // What the mod layers were when a model was prepared: their folders and the newest write time of the files that reach a model, so a changed mod is prepared again.
    string? _stamp;

    string Stamp => _stamp ??= MakeStamp();

    string StampFile(string name) => cache.ModelPack(name) + ".layers";

    string MakeStamp()
    {
        if (layers is null || layers.Count == 0) return "";
        long newest = 0;
        foreach (string layer in layers.Where(Directory.Exists))
            foreach (string folder in new[] { "Model", "Tex", "TexToGo", "Pack/Actor" })
            {
                string path = Path.Combine(layer, folder);
                if (!Directory.Exists(path)) continue;
                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    newest = Math.Max(newest, File.GetLastWriteTimeUtc(file).Ticks);
            }

        return string.Join('|', layers.Select(Path.GetFullPath)) + "#" + newest;
    }

    void WriteStamp(string name)
    {
        if (Stamp.Length == 0) File.Delete(StampFile(name));
        else File.WriteAllText(StampFile(name), Stamp);
    }
}
