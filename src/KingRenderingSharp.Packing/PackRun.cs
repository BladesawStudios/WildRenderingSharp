using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Packing;

/// <summary>Runs the pack preparer for one model or a list of them and reports in the preparer protocol.</summary>
sealed class PackRun(string romfs, CacheLayout cache, bool animations, bool force)
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

    /// <summary>Prepares one model, unless a manifest for it is already in the cache and <c>force</c> is not set.</summary>
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
        if (!force && modelFile is null && cache.IsPrepared(name))
        {
            log?.Invoke($"[prepare] {name} is already prepared.");
            return name;
        }

        PreparedPackModel prepared = PackPreparer.Prepare(romfs, name, cache.ModelDirectory(name), cache.Shaders, log, animations || animatedAnyway, animationPacks, modelFile);
        foreach (string problem in prepared.Problems) log?.Invoke("[prepare] " + problem);
        log?.Invoke($"[prepare] {prepared.Name}: {prepared.Shapes} shapes, {prepared.Programs} programs, {prepared.Textures} textures, {prepared.Animations} animations");
        return prepared.Name;
    }
}
