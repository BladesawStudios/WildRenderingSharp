using WildRenderingSharp;
using WildRenderingSharp.Hosting;
using WildRenderingSharp.Preparation;

// The preparer's command line - what OutOfProcessPreparer runs, and usable by hand:
//
//   WildRenderingSharp.Preparation ensure-system --romfs <dir> [--cache <dir>]
//   WildRenderingSharp.Preparation prepare --romfs <dir> --actor <name> [--cache <dir>]
//                                  [--mod <romfs dir>]... [--no-anims] [--force]
//
// --cache defaults to CacheLayout.DefaultRoot. --mod is a mod's romfs folder, repeated, highest
// priority first. `prepare` also builds the system assets, so a fresh cache needs nothing else.
//
// Progress goes to stdout as it happens. On success the last stdout line is
// "WRS_RESULT <resolved model name>" (prepare only) and the exit code is 0; on failure the error
// goes to stderr and the exit code is 1 (2 for a bad command line).

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
        string? romfs = null, actor = null, cacheRoot = null;
        var mods = new List<string>();
        bool importAnims = true, force = false;

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

    static void PrintUsage()
    {
        Console.WriteLine("WildRenderingSharp.Preparation - prepares actors from a romfs into a WildRenderingSharp cache.");
        Console.WriteLine();
        Console.WriteLine("  ensure-system --romfs <dir> [--cache <dir>]");
        Console.WriteLine("  prepare --romfs <dir> --actor <name> [--cache <dir>] [--mod <romfs dir>]... [--no-anims] [--force]");
        Console.WriteLine();
        Console.WriteLine($"  --cache defaults to {CacheLayout.DefaultRoot}");
    }
}
