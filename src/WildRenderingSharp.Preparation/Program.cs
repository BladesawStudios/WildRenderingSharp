using WildRenderingSharp;
using WildRenderingSharp.Hosting;
using WildRenderingSharp.Preparation;
using WildRenderingSharp.Preparation.Totk;
using WildRenderingSharp.Rom;

// ensure-system --romfs <dir> [--cache <dir>]
// prepare --romfs <dir> --actor <name> [--game totk|botw] [--cache <dir>] [--mod <romfs dir>]... [--no-anims] [--force]
// prepare-batch --romfs <dir> --list <file> [--cache <dir>] [--jobs <n>] [--mod <romfs dir>]... [--no-anims] [--force] [--verbose]
// prepare-bake --romfs <dir> [--list <file>] [--cache <dir>] [--jobs <n>] [--force]
//
// Progress goes to stdout; exit code 0 on success, 1 on failure (error on stderr), 2 for a bad command line. `prepare` ends with
// "WRS_RESULT <model>". The batch commands print WRS_BEGIN <name>, WRS_DONE <name>\t<model> and WRS_FAIL <name>\t<message> lines instead of progress,
// so a batch killed partway (a native decoder can abort the process) leaves the names begun but not finished as the suspects.

return Cli.Run(args);

static class Cli
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        string command = args[0];
        string? romfs = null, actor = null, cacheRoot = null, list = null, game = "totk";
        var mods = new List<string>();
        bool importAnims = true, force = false, verbose = false;
        int jobs = 0;

        for (int i = 1; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value");
            try
            {
                switch (a)
                {
                    case "--romfs": romfs = Next(); break;
                    case "--actor": actor = Next(); break;
                    case "--cache": cacheRoot = Next(); break;
                    case "--game": game = Next(); break;
                    case "--mod": mods.Add(Next()); break;
                    case "--no-anims": importAnims = false; break;
                    case "--force": force = true; break;
                    case "--list": list = Next(); break;
                    case "--jobs": jobs = int.TryParse(Next(), out int j) ? j : throw new ArgumentException("--jobs needs a number"); break;
                    case "--verbose": verbose = true; break;
                    default:
                        Console.Error.WriteLine($"Unknown argument '{a}'.");
                        PrintUsage();
                        return 2;
                }
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
        }

        if (string.IsNullOrEmpty(romfs) || !Directory.Exists(romfs))
        {
            Console.Error.WriteLine($"--romfs must name an existing directory (got '{romfs}').");
            return 2;
        }

        var cache = new CacheLayout(cacheRoot ?? CacheLayout.DefaultRoot);
        try
        {
            switch (command)
            {
                case "ensure-system":
                    ModelPreparer.EnsureSystemAssets(romfs, cache, Console.WriteLine);
                    return 0;

                case "prepare":
                    if (string.IsNullOrEmpty(actor))
                    {
                        Console.Error.WriteLine("prepare needs --actor <name>.");
                        return 2;
                    }
                    if (game == "botw")
                    {
                        string botwModel = WildRenderingSharp.Preparation.Botw.BotwModelPreparer.PrepareIfNeeded(romfs, actor, cache, Console.WriteLine, force);
                        Console.Out.Flush();
                        Console.WriteLine(OutOfProcessPreparer.ResultPrefix + botwModel);
                        return 0;
                    }
                    ModelPreparer.EnsureSystemAssets(romfs, cache, Console.WriteLine);
                    string model = ModelPreparer.PrepareIfNeeded(romfs, actor, cache, Console.WriteLine, importAnims, force, mods);
                    Console.Out.Flush();
                    Console.WriteLine(OutOfProcessPreparer.ResultPrefix + model);
                    return 0;

                case "prepare-batch":
                    if (string.IsNullOrEmpty(list) || !File.Exists(list))
                    {
                        Console.Error.WriteLine($"prepare-batch needs --list <file> naming an existing file (got '{list}').");
                        return 2;
                    }
                    return PrepareBatch(romfs, cache, File.ReadAllLines(list), mods, jobs, importAnims, force, verbose);

                case "prepare-bake":
                    return PrepareBake(romfs, cache, list is not null && File.Exists(list) ? File.ReadAllLines(list) : [], jobs, force);

                default:
                    Console.Error.WriteLine($"Unknown command '{command}'.");
                    PrintUsage();
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    static int PrepareBatch(string romfs, CacheLayout cache, string[] lines, List<string> mods, int jobs,
        bool importAnims, bool force, bool verbose)
    {
        var names = lines.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        int parallelism = jobs > 0 ? jobs : PrepareBatchRequest.DefaultParallelism;

        TextWriter protocol = Console.Out;
        var gate = new object();
        void Say(string line)
        {
            lock (gate)
            {
                protocol.WriteLine(line);
                protocol.Flush();
            }
        }

        try
        {
            ModelPreparer.EnsureSystemAssets(romfs, cache, verbose ? Console.WriteLine : null);
        }
        catch (Exception ex)
        {
            // Every name fails with the reason, instead of the batch dying and being retried.
            string reason = string.Join(' ', ex.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
            foreach (string name in names)
                Say($"WRS_FAIL {name}\t{reason}");
            return 0;
        }
        if (!verbose)
            Console.SetOut(TextWriter.Null);

        ModelPreparer.PrepareMany(romfs, names, cache, parallelism,
            name => Say($"WRS_BEGIN {name}"),
            outcome => Say(outcome.Succeeded
                ? $"WRS_DONE {outcome.ActorOrModelName}	{outcome.ModelName}"
                : $"WRS_FAIL {outcome.ActorOrModelName}	{outcome.Error}"),
            importAnims, force, mods);
        return 0;
    }

    static int PrepareBake(string romfs, CacheLayout cache, string[] lines, int jobs, bool force)
    {
        int parallelism = jobs > 0 ? jobs : PrepareBatchRequest.DefaultParallelism;
        string dir = cache.Bake;
        using var rom = TotkRom.Open(romfs);
        if (force || !File.Exists(Path.Combine(dir, TotkBake.IndexFile)))
            TotkBake.BuildIndex(rom, dir, parallelism);
        Console.Out.Flush();

        var gate = new object();
        void Say(string line)
        {
            lock (gate)
            {
                Console.WriteLine(line);
                Console.Out.Flush();
            }
        }
        var tiles = lines.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        Parallel.ForEach(tiles, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, tile =>
        {
            Say($"WRS_BEGIN {tile}");
            try
            {
                if (force || !File.Exists(Path.Combine(dir, tile + ".json")))
                    TotkBakeTile.Export(rom, tile, dir);
                Say($"WRS_DONE {tile}	{tile}");
            }
            catch (Exception ex)
            {
                Say($"WRS_FAIL {tile}	{ex.Message.ReplaceLineEndings(" ")}");
            }
        });
        return 0;
    }

    static void PrintUsage()
    {
        Console.WriteLine("WildRenderingSharp.Preparation - prepares actors from a romfs into a WildRenderingSharp cache.");
        Console.WriteLine();
        Console.WriteLine("  ensure-system --romfs <dir> [--cache <dir>]");
        Console.WriteLine("  prepare --romfs <dir> --actor <name> [--game totk|botw] [--cache <dir>] [--mod <romfs dir>]... [--no-anims] [--force]");
        Console.WriteLine("  prepare-batch --romfs <dir> --list <file> [--cache <dir>] [--jobs <n>] [--mod <romfs dir>]... [--no-anims] [--force] [--verbose]");
        Console.WriteLine("  prepare-bake --romfs <dir> [--list <file>] [--cache <dir>] [--jobs <n>] [--force]");
        Console.WriteLine();
        Console.WriteLine($"  --cache defaults to {CacheLayout.DefaultRoot}");
    }
}
