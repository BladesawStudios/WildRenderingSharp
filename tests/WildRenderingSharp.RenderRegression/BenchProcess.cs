using System.Diagnostics;

namespace WildRenderingSharp.RenderRegression;

/// <summary>Renders one scene by running the test bench in a process of its own, so no GL state carries from one scene to the next.</summary>
sealed class BenchProcess(string benchDll)
{
    // Clouds hold still so a scene renders the same every time.
    static readonly Dictionary<string, string> Pinned = new() { ["static-clouds"] = "1" };

    public static BenchProcess Locate(string projectDirectory)
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;
        string dll = Path.Combine(projectDirectory, "..", "WildRenderingSharp.TestBench", "bin", configuration, "net10.0", "WildRenderingSharp.TestBench.dll");
        return File.Exists(dll) ? new BenchProcess(Path.GetFullPath(dll))
            : throw new FileNotFoundException($"The test bench is not built at '{Path.GetFullPath(dll)}'.");
    }

    // Returns null when the scene rendered, else why it did not.
    public string? Render(Scene scene, RunSettings settings, string outputPath)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in Arguments(scene, settings, outputPath))
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        // A nearly blank image (exit 4) is a finding for the comparison, not a failure to render; a dark scene has few colours.
        return process.ExitCode is 0 or 4 ? null : $"exit code {process.ExitCode}: {output.Split('\n').LastOrDefault(l => l.Trim().Length > 0)?.Trim()}";
    }

    IEnumerable<string> Arguments(Scene scene, RunSettings settings, string outputPath)
    {
        yield return benchDll;
        foreach (var (name, value) in new Dictionary<string, string>
        {
            ["game"] = scene.Game, ["romfs"] = settings.RomfsFor(scene.Game)!, ["actor"] = scene.Actor,
            ["size"] = settings.Size.ToString(), ["out"] = outputPath,
        })
        {
            yield return "--" + name;
            yield return value;
        }
        if (settings.CacheFor(scene.Game) is { } cache)
        {
            yield return "--cache";
            yield return cache;
        }
        var options = scene.Game == "totk" ? Pinned.Concat(scene.Options) : scene.Options;
        foreach (var (name, value) in options)
        {
            yield return "--" + name;
            yield return value;
        }
    }
}
