using WildRenderingSharp.Hosting.Preparers;
using WildRenderingSharp.Preparation.Botw;
using WildRenderingSharp.Preparation.Totk;
using WildRenderingSharp.Rom.Games;
using WildRenderingSharp.Storage;

namespace WildRenderingSharp.Preparation;

/// <summary>Runs one preparation command; the exit code is 0 for success, 1 for a failure, and 2 for a bad command line.</summary>
static class Cli
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (CliUsageException ex)
        {
            return BadUsage(ex.Message, ex.ShowUsage);
        }
        if (string.IsNullOrEmpty(options.Romfs) || !Directory.Exists(options.Romfs))
            return BadUsage($"--romfs must name an existing directory (got '{options.Romfs}').");

        var cache = new CacheLayout(options.CacheRoot ?? CacheLayout.DefaultRoot);
        try
        {
            return Dispatch(options, options.Romfs, cache);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    static int Dispatch(CliOptions options, string romfs, CacheLayout cache)
    {
        switch (options.Command)
        {
            case "ensure-system":
                ModelPreparer.EnsureSystemAssets(romfs, cache, Console.WriteLine);
                return 0;
            case "prepare":
                return string.IsNullOrEmpty(options.Actor) ? BadUsage("prepare needs --actor <name>.") : Prepare(options, options.Actor, romfs, cache);
            case "prepare-batch":
                if (string.IsNullOrEmpty(options.List)) {
                    return BadUsage("You must provide a text file with a list of files to be processed, using --list <file>.", true);
                }

                if (!File.Exists(options.List)) {
                    return BadUsage($"The list file you specified ('{options.List}') doesn't exist.");
                }
                
                return PrepareBatch(options, romfs, cache);
            case "prepare-bake":
                return PrepareBake(options, romfs, cache);
            default:
                return BadUsage($"Unknown command '{options.Command}'.", showUsage: true);
        }
    }

    static int Prepare(CliOptions options, string actor, string romfs, CacheLayout cache)
    {
        string model;
        if (options.Game == "botw")
        {
            model = BotwModelPreparer.PrepareIfNeeded(romfs, actor, cache, Console.WriteLine, options.Force);
        }
        else
        {
            ModelPreparer.EnsureSystemAssets(romfs, cache, Console.WriteLine);
            model = ModelPreparer.PrepareIfNeeded(romfs, actor, cache, Console.WriteLine, options.ImportAnims, options.Force, options.Mods);
        }
        Console.Out.Flush();
        Console.WriteLine(OutOfProcessPreparer.ResultPrefix + model);
        return 0;
    }

    static int PrepareBatch(CliOptions options, string romfs, CacheLayout cache)
    {
        var names = DistinctLines(File.ReadAllLines(options.List!));
        int parallelism = options.Jobs > 0 ? options.Jobs : PrepareBatchRequest.DefaultParallelism;
        var protocol = new ProtocolWriter(Console.Out);

        try
        {
            ModelPreparer.EnsureSystemAssets(romfs, cache, options.Verbose ? Console.WriteLine : null);
        }
        catch (Exception ex)
        {
            protocol.Say("Failed to export system assets, no actor assets will be processed.");
            // Print an explicit failure message for each actor, to make things
            // easier for those parsing our output.
            string reason = string.Join(' ', ex.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
            foreach (string name in names)
                protocol.Say($"WRS_FAIL {name}\t{reason}");
            return 0;
        }
        if (!options.Verbose)
            Console.SetOut(TextWriter.Null);

        ModelPreparer.PrepareMany(romfs, names, cache, parallelism,
            name => protocol.Say($"WRS_BEGIN {name}"),
            outcome => protocol.Say(outcome.Succeeded
                ? $"WRS_DONE {outcome.ActorOrModelName}\t{outcome.ModelName}"
                : $"WRS_FAIL {outcome.ActorOrModelName}\t{outcome.Error}"),
            options.ImportAnims, options.Force, options.Mods);
        return 0;
    }

    static int PrepareBake(CliOptions options, string romfs, CacheLayout cache)
    {
        int parallelism = options.Jobs > 0 ? options.Jobs : PrepareBatchRequest.DefaultParallelism;
        string dir = cache.Bake;
        using var rom = TotkRom.Open(romfs);
        if (options.Force || !File.Exists(Path.Combine(dir, TotkBake.IndexFile)))
            TotkBake.BuildIndex(rom, dir, parallelism);
        Console.Out.Flush();

        System.Diagnostics.Debug.Assert(options.List is not null);
        System.Diagnostics.Debug.Assert(File.Exists(options.List));
        
        var protocol = new ProtocolWriter(Console.Out);
        var files = DistinctLines(File.ReadAllLines(options.List));
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, file =>
        {
            protocol.Say($"WRS_BEGIN {file}");
            try
            {
                string manifestFile = Path.Combine(dir, file + ".json");
                if (options.Force || !File.Exists(manifestFile)) {
                    TotkBakeTile.Export(rom, file, dir);
                }

                protocol.Say($"WRS_DONE {file}\t{file}");
            }
            catch (Exception ex)
            {
                protocol.Say($"WRS_FAIL {file}\t{ex.Message.ReplaceLineEndings(" ")}");
            }
        });
        return 0;
    }

    static List<string> DistinctLines(string[] lines) =>
        lines.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.Ordinal).ToList();

    static int BadUsage(string message, bool showUsage = false)
    {
        Console.Error.WriteLine(message);
        if (showUsage)
            PrintUsage();
        return 2;
    }

    static void PrintUsage()
    {
        Console.WriteLine("WildRenderingSharp.Preparation - unpacks actor assets from romfs to a format convenient for rendering in the cache folder.");
        Console.WriteLine();
        Console.WriteLine("  ensure-system --romfs <dir> [--cache <dir>]");
        Console.WriteLine("  prepare --romfs <dir> --actor <name> [--game totk|botw] [--cache <dir>] [--mod <romfs dir>]... [--no-anims] [--force]");
        Console.WriteLine("  prepare-batch --romfs <dir> --list <file> [--cache <dir>] [--jobs <n>] [--mod <romfs dir>]... [--no-anims] [--force] [--verbose]");
        Console.WriteLine("  prepare-bake --romfs <dir> [--list <file>] [--cache <dir>] [--jobs <n>] [--force]");
        Console.WriteLine();
        Console.WriteLine($"  --cache defaults to {CacheLayout.DefaultRoot}");
    }
}
