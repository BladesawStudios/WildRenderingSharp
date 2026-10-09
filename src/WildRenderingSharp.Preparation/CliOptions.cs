namespace WildRenderingSharp.Preparation;

/// <summary>A command line that cannot be run, with whether the usage text should follow the message.</summary>
sealed class CliUsageException(string message, bool showUsage = false) : Exception(message)
{
    public bool ShowUsage { get; } = showUsage;
}

/// <summary>The command and options of a preparation command line.</summary>
sealed record CliOptions(string Command, string? Romfs, string? Actor, string? CacheRoot, string? List, string Game,
    IReadOnlyList<string> Mods, bool ImportAnims, bool Force, bool Verbose, int Jobs)
{
    public static CliOptions Parse(string[] args)
    {
        string? romfs = null, actor = null, cacheRoot = null, list = null, game = "totk";
        var mods = new List<string>();
        bool importAnims = true, force = false, verbose = false;
        int jobs = 0;

        for (int i = 1; i < args.Length; i++)
        {
            string option = args[i];
            string Value() => i + 1 < args.Length ? args[++i] : throw new CliUsageException($"{option} needs a value");
            switch (option)
            {
                case "--romfs": romfs = Value(); break;
                case "--actor": actor = Value(); break;
                case "--cache": cacheRoot = Value(); break;
                case "--game": game = Value(); break;
                case "--mod": mods.Add(Value()); break;
                case "--no-anims": importAnims = false; break;
                case "--force": force = true; break;
                case "--list": list = Value(); break;
                case "--jobs": jobs = int.TryParse(Value(), out int parsed) ? parsed : throw new CliUsageException("--jobs needs a number"); break;
                case "--verbose": verbose = true; break;
                default: throw new CliUsageException($"Unknown argument '{option}'.", showUsage: true);
            }
        }
        return new CliOptions(args[0], romfs, actor, cacheRoot, list, game, mods, importAnims, force, verbose, jobs);
    }
}
