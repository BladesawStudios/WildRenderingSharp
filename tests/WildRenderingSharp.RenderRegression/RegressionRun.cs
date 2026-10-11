using WildRenderingSharp.Imaging;

namespace WildRenderingSharp.RenderRegression;

/// <summary>Renders each scene and either keeps the image as the baseline or compares it with the one kept.</summary>
sealed class RegressionRun(RunSettings settings, BenchProcess bench)
{
    public int Execute(IReadOnlyList<Scene> scenes)
    {
        Directory.CreateDirectory(settings.OutputDirectory);
        if (settings.Command == "record")
            Directory.CreateDirectory(settings.GoldenDirectory);

        int failed = 0, skipped = 0;
        foreach (var scene in scenes)
        {
            string? outcome = settings.RomfsFor(scene.Game) is null ? Skip(scene.Game) : RunScene(scene);
            Console.WriteLine($"{scene.Name,-18} {outcome ?? "ok"}");
            if (outcome is null)
                continue;
            if (outcome.StartsWith("skipped", StringComparison.Ordinal))
                skipped++;
            else
                failed++;
        }

        Console.WriteLine($"\n{scenes.Count - failed - skipped} ok, {failed} failed, {skipped} skipped");
        return failed == 0 ? 0 : 1;
    }

    static string Skip(string game) => $"skipped: no {game} romfs given";

    // Null when the scene passed; otherwise why not.
    string? RunScene(Scene scene)
    {
        string rendered = Path.Combine(settings.OutputDirectory, scene.Name + ".png");
        if (bench.Render(scene, settings, rendered) is { } error)
            return "FAILED to render: " + error;

        string golden = Path.Combine(settings.GoldenDirectory, scene.Name + ".png");
        if (settings.Command == "record")
        {
            File.Copy(rendered, golden, overwrite: true);
            return null;
        }
        return File.Exists(golden) ? Compare(scene, golden, rendered) : "FAILED: no baseline, run 'record' first";
    }

    string? Compare(Scene scene, string golden, string rendered)
    {
        Image baseline = PngReader.Read(golden), current = PngReader.Read(rendered);
        var stats = ImageDiff.Compare(baseline, current);
        if (stats.Within(settings.Tolerance))
            return null;

        string diffPath = Path.Combine(settings.OutputDirectory, scene.Name + ".diff.png");
        if (baseline.Width == current.Width && baseline.Height == current.Height)
            PngWriter.WriteRgba(diffPath, baseline.Width, baseline.Height, ImageDiff.Visualize(baseline, current).Rgba);
        return $"FAILED: mean {stats.MeanAbs:F3}/255, {stats.FractionOver16:P3} of pixels visibly different (difference image: {diffPath})";
    }
}
