using KingRenderingSharp.Core.Storage;
using KingRenderingSharp.Packing;

// [prepare] --romfs <dir> --actor <model> [--anim <pack>]... [--cache <dir>] [--no-anims] [--force]
// prepare-batch --romfs <dir> --list <file> [--cache <dir>] [--jobs <n>] [--no-anims] [--force]
// system --romfs <dir> [--cache <dir>] [--list <bake tiles>] writes the data that belongs to no model (the cloud dome masks, the bake index and the listed bake tiles).
// The first two answer in the protocol the model preparer's host reads: KRS_RESULT, KRS_BEGIN, KRS_DONE and KRS_FAIL lines.
string command = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : "prepare";
string? romfs = null, actor = null, cache = null, list = null, modelFile = null;
List<string> animationPacks = [];
bool animations = true, force = false;
int jobs = 0;
for (int i = command == args.FirstOrDefault() ? 1 : 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--romfs" when i + 1 < args.Length: romfs = args[++i]; break;
        case "--actor" when i + 1 < args.Length: actor = args[++i]; break;
        case "--cache" when i + 1 < args.Length: cache = args[++i]; break;
        case "--list" when i + 1 < args.Length: list = args[++i]; break;
        case "--model-file" when i + 1 < args.Length: modelFile = args[++i]; break;
        case "--anim" when i + 1 < args.Length: animationPacks.Add(args[++i]); break;
        case "--jobs" when i + 1 < args.Length && int.TryParse(args[i + 1], out int count): jobs = count; i++; break;
        case "--no-anims": animations = false; break;
        case "--force": force = true; break;
        default:
            Console.Error.WriteLine("usage: [prepare] --romfs <dir> --actor <model> [--anim <pack>]... [--cache <dir>] [--no-anims] [--force]\n       prepare-batch --romfs <dir> --list <file> [--cache <dir>] [--jobs <n>] [--no-anims] [--force]");
            return 2;
    }
}

if (string.IsNullOrEmpty(romfs) || !Directory.Exists(romfs))
{
    Console.Error.WriteLine("--romfs must name an existing directory.");
    return 2;
}

CacheLayout layout = new(cache ?? CacheLayout.DefaultRoot);
PackRun run = new(romfs, layout, animations, force);
switch (command)
{
    case "prepare" when !string.IsNullOrEmpty(actor):
        return run.One(actor, animationPacks, modelFile);
    case "system":
        return SystemExporter.Export(romfs, layout, string.IsNullOrEmpty(list) || !File.Exists(list) ? [] : [.. File.ReadAllLines(list).Select(l => l.Trim()).Where(l => l.Length > 0).Distinct()], jobs, Console.WriteLine);
    case "prepare-batch" when !string.IsNullOrEmpty(list) && File.Exists(list):
        return run.Batch(File.ReadAllLines(list), jobs);
    default:
        Console.Error.WriteLine("prepare needs --actor <model>; prepare-batch needs --list <existing file>.");
        return 2;
}
