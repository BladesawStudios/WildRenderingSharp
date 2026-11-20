using KingRenderingSharp.Core.Storage;
using KingRenderingSharp.Packing;

// The offline half of the renderer: turns a game's romfs into the cache it reads. The commands are the preparer protocol's, which a host runs this as a child process for.
//   ensure-system --romfs <dir> [--cache <dir>]                      builds the system pack (the data that belongs to no model) unless the cache has it
//   prepare --romfs <dir> --actor <name> [--cache <dir>] [--mod <dir>]... [--no-anims] [--force] [--model-file <bfres>]
//   prepare-batch --romfs <dir> --list <file> [--cache <dir>] [--mod <dir>]... [--jobs <n>] [--no-anims] [--force]
//   prepare-bake --romfs <dir> [--list <file>] [--cache <dir>] [--jobs <n>] [--force]
// prepare and prepare-batch answer in the protocol: KRS_RESULT, KRS_BEGIN, KRS_DONE and KRS_FAIL lines. --game is accepted and ignored: the dump says which game it is.
const string Usage = "usage: ensure-system --romfs <dir> [--cache <dir>]\n"
    + "       prepare --romfs <dir> --actor <name> [--cache <dir>] [--mod <dir>]... [--no-anims] [--force] [--model-file <bfres>]\n"
    + "       prepare-batch --romfs <dir> --list <file> [--cache <dir>] [--mod <dir>]... [--jobs <n>] [--no-anims] [--force]\n"
    + "       prepare-bake --romfs <dir> [--list <file>] [--cache <dir>] [--jobs <n>] [--force]";

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 2 : 0;
}

// serve: stays running and takes one command a line from standard input (its arguments separated by tabs), answering each as a run of its own and ending the answer with KRS_END and the exit code. A host that
// prepares model after model starts it once, so the process is started, its code compiled and the shader archives it has read are paid for once and not for every model.
if (args[0] == "serve")
{
    string? request;
    while ((request = Console.In.ReadLine()) is not null)
    {
        if (request.Length == 0) continue;
        int code = Execute(request.Split('	'));
        Console.WriteLine("KRS_END " + code);
        Console.Out.Flush();
    }

    return 0;
}

return Execute(args);

int Execute(string[] argv)
{
    string command = argv[0];
    string? romfs = null, actor = null, cache = null, list = null, modelFile = null;
    List<string> animationPacks = [], mods = [];
    bool animations = true, force = false;
    int jobs = 0;
    for (int i = 1; i < argv.Length; i++)
    {
        switch (argv[i])
        {
            case "--romfs" when i + 1 < argv.Length: romfs = argv[++i]; break;
            case "--actor" when i + 1 < argv.Length: actor = argv[++i]; break;
            case "--cache" when i + 1 < argv.Length: cache = argv[++i]; break;
            case "--list" when i + 1 < argv.Length: list = argv[++i]; break;
            case "--model-file" when i + 1 < argv.Length: modelFile = argv[++i]; break;
            case "--mod" when i + 1 < argv.Length: mods.Add(argv[++i]); break;
            case "--anim" when i + 1 < argv.Length: animationPacks.Add(argv[++i]); break;
            case "--game" when i + 1 < argv.Length: i++; break;
            case "--jobs" when i + 1 < argv.Length && int.TryParse(argv[i + 1], out int count): jobs = count; i++; break;
            case "--no-anims": animations = false; break;
            case "--force": force = true; break;
            case "--verbose": break;
            default:
                Console.Error.WriteLine($"Unknown argument '{argv[i]}'.\n{Usage}");
                return 2;
        }
    }

    if (string.IsNullOrEmpty(romfs) || !Directory.Exists(romfs))
    {
        Console.Error.WriteLine($"--romfs must name an existing directory (got '{romfs}').");
        return 2;
    }

    CacheLayout layout = new(cache ?? CacheLayout.DefaultRoot);
    PackRun run = new(romfs, layout, animations, force, mods.Where(Directory.Exists).ToList());
    void Say(string line)
    {
        Console.WriteLine(line);
        Console.Out.Flush();
    }

    try
    {
        switch (command)
        {
            case "ensure-system":
                return EnsureSystem.Run(romfs, layout, Say);
            case "prepare" when !string.IsNullOrEmpty(actor):
                if (EnsureSystem.Run(romfs, layout, Say) is var system and not 0) return system;
                return run.One(actor, animationPacks, modelFile);
            case "prepare-bake" or "bake":
                return BakeExporter.Run(romfs, layout, string.IsNullOrEmpty(list) || !File.Exists(list) ? [] : [.. File.ReadAllLines(list).Select(l => l.Trim()).Where(l => l.Length > 0).Distinct()], jobs, force, Say);
            case "prepare-batch" when !string.IsNullOrEmpty(list) && File.Exists(list):
                string[] lines = File.ReadAllLines(list);
                try
                {
                    if (EnsureSystem.Run(romfs, layout, Say) is not 0) throw new InvalidOperationException("The system assets could not be built.");
                }
                catch (Exception exception)
                {
                    Say("Failed to export system assets, no actor assets will be processed.");
                    // An explicit failure for each name, to make the output easier to parse.
                    string reason = exception.Message.ReplaceLineEndings(" ");
                    foreach (string name in lines.Select(l => l.Trim().TrimStart('+').Split('|')[0].Trim()).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal))
                        Say($"KRS_FAIL {name}\t{reason}");
                    return 0;
                }

                return run.Batch(lines, jobs);
            default:
                Console.Error.WriteLine($"{command} needs its arguments.\n{Usage}");
                return 2;
        }
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception);
        return 1;
    }
}
