using KingRenderingSharp.Core.Storage;
using KingRenderingSharp.Packing;

// pack --romfs <dir> --actor <name> [--cache <dir>] [--no-anims]
string? romfs = null, actor = null, cache = null;
bool animations = true;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--romfs" when i + 1 < args.Length: romfs = args[++i]; break;
        case "--actor" when i + 1 < args.Length: actor = args[++i]; break;
        case "--cache" when i + 1 < args.Length: cache = args[++i]; break;
        case "--no-anims": animations = false; break;
        default:
            Console.Error.WriteLine("usage: pack --romfs <dir> --actor <name> [--cache <dir>] [--no-anims]");
            return 2;
    }
}

if (string.IsNullOrEmpty(romfs) || !Directory.Exists(romfs) || string.IsNullOrEmpty(actor))
{
    Console.Error.WriteLine("pack needs --romfs <existing directory> and --actor <name>.");
    return 2;
}

CacheLayout layout = new(cache ?? CacheLayout.DefaultRoot);
try
{
    PreparedPackModel prepared = PackPreparer.Prepare(romfs, actor, layout.ModelDirectory(actor), layout.Shaders, Console.WriteLine, animations);
    foreach (string problem in prepared.Problems) Console.Error.WriteLine(problem);
    Console.WriteLine($"{prepared.Name}: {prepared.Shapes} shapes, {prepared.Programs} programs, {prepared.Textures} textures, {prepared.Animations} animations");
    return prepared.Problems.Count == 0 ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
