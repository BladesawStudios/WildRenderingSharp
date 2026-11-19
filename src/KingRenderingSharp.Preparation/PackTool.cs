using System.Diagnostics;
using System.Globalization;
using KingRenderingSharp.Hosting.Preparers;
using KingRenderingSharp.Core.Storage;
using KingRenderingSharp.Preparation.Totk;

namespace KingRenderingSharp.Preparation;

/// <summary>Prepares models through the pack tool (BfShaderSharp's asset packs), a process of its own because its shader translator cannot share this one with ShaderLibrary's.</summary>
/// <remarks>The actor is resolved to its model and animation packs here, where the actor info is read; the tool is given the model and the packs, and answers in the preparer protocol.</remarks>
static class PackTool
{
    public static string? Executable()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "krs-pack");
        string exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "KingRenderingSharp.Packing.exe" : "KingRenderingSharp.Packing");
        if (File.Exists(exe)) return exe;
        string dll = Path.Combine(dir, "KingRenderingSharp.Packing.dll");
        return File.Exists(dll) ? dll : null;
    }

    /// <summary>The model and animation packs of an actor name; Breath of the Wild names are the models themselves.</summary>
    public static (string Model, IReadOnlyList<string> Packs) Resolve(string romfs, string name, string game)
    {
        if (game == "botw") return (name, []);
        using TotkRomfs rom = new(romfs);
        TotkActorInfo.Resolved? actor = TotkActorInfo.Resolve(rom.Layered, name);
        return (actor?.ModelName ?? name, actor?.AnimPackNames ?? []);
    }

    /// <summary>Runs the tool with the arguments, passing each of its output lines to <paramref name="onLine"/>; returns its exit code.</summary>
    public static int Run(IEnumerable<string> arguments, Action<string> onLine)
    {
        string exe = Executable() ?? throw new FileNotFoundException("The pack tool (krs-pack/KingRenderingSharp.Packing) is not beside the preparer; build KingRenderingSharp.Packing, or pass --legacy.");
        ProcessStartInfo info = new() { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        if (exe.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            info.FileName = "dotnet";
            info.ArgumentList.Add(exe);
        }
        else info.FileName = exe;
        foreach (string argument in arguments) info.ArgumentList.Add(argument);

        using Process process = Process.Start(info) ?? throw new InvalidOperationException("The pack tool did not start.");
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
        process.BeginErrorReadLine();
        string? line;
        while ((line = process.StandardOutput.ReadLine()) is not null) onLine(line);
        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>Prepares one model through the tool and returns the model name it reports; a failure throws with the tool's output.</summary>
    public static string PrepareModel(string romfs, string name, CacheLayout cache, bool animations, bool force, Action<string>? log)
    {
        // The game is told by the dump: a Breath of the Wild romfs has no Tears of the Kingdom actor info.
        string game = Directory.EnumerateFiles(Path.Combine(romfs, "Pack"), "*.pack.zs").Any() ? "totk" : "botw";
        var (model, packs) = Resolve(romfs, name, game);
        List<string> arguments = ["prepare", "--romfs", romfs, "--actor", model, "--cache", cache.Root];
        foreach (string pack in packs) arguments.AddRange(["--anim", pack]);
        if (!animations) arguments.Add("--no-anims");
        if (force) arguments.Add("--force");
        string? result = null;
        List<string> tail = [];
        int exit = Run(arguments, line =>
        {
            if (line.StartsWith(PreparerProtocol.ResultPrefix, StringComparison.Ordinal)) result = line[PreparerProtocol.ResultPrefix.Length..].Trim();
            else
            {
                tail.Add(line);
                log?.Invoke(line);
            }
        });
        return exit == 0 && result is not null ? result : throw new InvalidOperationException($"The pack tool failed for '{name}' (exit code {exit}): {string.Join(' ', tail.TakeLast(10))}");
    }

    public static int Prepare(CliOptions options, string actor, string romfs, CacheLayout cache)
    {
        var (model, packs) = Resolve(romfs, actor, options.Game);
        List<string> arguments = ["prepare", "--romfs", romfs, "--actor", model, "--cache", cache.Root];
        foreach (string pack in packs) arguments.AddRange(["--anim", pack]);
        if (!options.ImportAnims) arguments.Add("--no-anims");
        if (options.Force) arguments.Add("--force");
        return Run(arguments, Console.WriteLine);
    }

    public static int PrepareBatch(CliOptions options, string romfs, CacheLayout cache, IReadOnlyList<string> lines, ProtocolWriter protocol)
    {
        // The tool is given models; its progress comes back under the model names, which are turned back into the names asked for.
        Dictionary<string, List<string>> askedFor = new(StringComparer.Ordinal);
        List<string> resolved = [];
        foreach (string raw in lines)
        {
            bool animated = raw.StartsWith('+');
            string name = animated ? raw[1..].Trim() : raw;
            try
            {
                var (model, packs) = Resolve(romfs, name, options.Game);
                if (!askedFor.TryGetValue(model, out List<string>? names)) askedFor[model] = names = [];
                names.Add(name);
                if (names.Count == 1) resolved.Add((animated ? "+" : "") + model + (packs.Count > 0 ? "|" + string.Join(',', packs) : ""));
            }
            catch (Exception exception)
            {
                protocol.Say($"{PreparerProtocol.FailPrefix}{name}\t{exception.GetBaseException().Message.ReplaceLineEndings(" ")}");
            }
        }

        string list = Path.Combine(Path.GetTempPath(), $"krs-packlist-{Environment.ProcessId}-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(list, resolved);
        try
        {
            List<string> arguments = ["prepare-batch", "--romfs", romfs, "--cache", cache.Root, "--list", list];
            if (options.Jobs > 0) arguments.AddRange(["--jobs", options.Jobs.ToString(CultureInfo.InvariantCulture)]);
            if (!options.ImportAnims) arguments.Add("--no-anims");
            if (options.Force) arguments.Add("--force");
            return Run(arguments, line => Relay(line, askedFor, protocol));
        }
        finally
        {
            File.Delete(list);
        }
    }

    static void Relay(string line, Dictionary<string, List<string>> askedFor, ProtocolWriter protocol)
    {
        foreach (string prefix in new[] { PreparerProtocol.BeginPrefix, PreparerProtocol.DonePrefix, PreparerProtocol.FailPrefix })
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string rest = line[prefix.Length..];
            int tab = rest.IndexOf('\t');
            string model = tab < 0 ? rest : rest[..tab];
            foreach (string name in askedFor.GetValueOrDefault(model) ?? [model]) protocol.Say(prefix + name + (tab < 0 ? "" : rest[tab..]));
            return;
        }

        protocol.Say(line);
    }
}
