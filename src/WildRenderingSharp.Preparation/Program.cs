using WildRenderingSharp;
using WildRenderingSharp.Hosting;
using WildRenderingSharp.Preparation;

// The preparer's command line - what OutOfProcessPreparer runs, and usable by hand:
//
//   WildRenderingSharp.Preparation ensure-system --romfs <dir> [--cache <dir>]
//   WildRenderingSharp.Preparation prepare --romfs <dir> --actor <name> [--cache <dir>]
//                                  [--mod <romfs dir>]... [--no-anims] [--force]
//   WildRenderingSharp.Preparation prepare-batch --romfs <dir> --list <file> [--cache <dir>]
//                                  [--jobs <n>] [--mod <romfs dir>]... [--no-anims] [--force] [--verbose]
//   WildRenderingSharp.Preparation prepare-bake --romfs <dir> [--list <file>] [--cache <dir>] [--jobs <n>] [--force]
//
// --cache defaults to CacheLayout.DefaultRoot. --mod is a mod's romfs folder, repeated, highest
// priority first. `prepare` also builds the system assets, so a fresh cache needs nothing else.
//
// Progress goes to stdout as it happens. On success the last stdout line is
// "WRS_RESULT <resolved model name>" (prepare only) and the exit code is 0; on failure the error
// goes to stderr and the exit code is 1 (2 for a bad command line).
//
// prepare-batch prepares every name in --list (one per line), --jobs at a time. Its stdout is a
// line protocol rather than progress - the per-model chatter is dropped unless --verbose:
//   WRS_BEGIN <name>                 a name has started
//   WRS_DONE  <name>	<model>        it is prepared (or was already up to date)
//   WRS_FAIL  <name>	<message>      it failed; the batch carries on
// prepare-bake builds the baked-lighting index (<cache>/_bake/index.bin) if it is missing, then
// exports every bake tile named in --list (one per line), speaking the same protocol with the tile
// name in place of the model.
// A batch that exits non-zero was killed partway - a native decoder can abort the whole process -
// and the names begun but not finished are the suspects (OutOfProcessPreparer retries the rest).

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
        string? romfs = null, actor = null, cacheRoot = null, list = null;
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
                    ModelPreparer.SetModRomfsLayers(mods);
                    ModelPreparer.EnsureSystemAssets(romfs, cache, Console.WriteLine);
                    string model = ModelPreparer.PrepareIfNeeded(romfs, actor, cache, Console.WriteLine, importAnims, force);
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

        ModelPreparer.SetModRomfsLayers(mods);
        ModelPreparer.EnsureSystemAssets(romfs, cache, verbose ? Console.WriteLine : null);
        if (!verbose)
            Console.SetOut(TextWriter.Null);

        ModelPreparer.PrepareMany(romfs, names, cache, parallelism,
            name => Say($"WRS_BEGIN {name}"),
            outcome => Say(outcome.Succeeded
                ? $"WRS_DONE {outcome.ActorOrModelName}	{outcome.ModelName}"
                : $"WRS_FAIL {outcome.ActorOrModelName}	{outcome.Error}"),
            importAnims, force);
        return 0;
    }

    static int PrepareBake(string romfs, CacheLayout cache, string[] lines, int jobs, bool force)
    {
        int parallelism = jobs > 0 ? jobs : PrepareBatchRequest.DefaultParallelism;
        string dir = cache.Bake;
        if (force || !File.Exists(Path.Combine(dir, ShaderLibrary.CompileTool.ExportBake.IndexFile)))
            ShaderLibrary.CompileTool.ExportBake.BuildIndex(romfs, dir, parallelism);
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
                    ShaderLibrary.CompileTool.ExportBake.ExportTile(romfs, tile, dir);
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
        Console.WriteLine("  prepare --romfs <dir> --actor <name> [--cache <dir>] [--mod <romfs dir>]... [--no-anims] [--force]");
        Console.WriteLine("  prepare-batch --romfs <dir> --list <file> [--cache <dir>] [--jobs <n>] [--mod <romfs dir>]... [--no-anims] [--force] [--verbose]");
        Console.WriteLine("  prepare-bake --romfs <dir> [--list <file>] [--cache <dir>] [--jobs <n>] [--force]");
        Console.WriteLine();
        Console.WriteLine($"  --cache defaults to {CacheLayout.DefaultRoot}");
    }
}
