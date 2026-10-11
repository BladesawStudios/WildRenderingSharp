namespace WildRenderingSharp.RenderRegression;

/// <summary>Where the ROM dumps, caches and image folders are, from the command line or the <c>WRS_*</c> environment variables.</summary>
sealed record RunSettings(string Command, string? TotkRomfs, string? BotwRomfs, string? TotkCache, string? BotwCache, string? Only,
    int Size, string GoldenDirectory, string OutputDirectory, DiffStats Tolerance)
{
    public const string Usage = """
        render-regression <record|check> [options]

          record   render every scene and keep the images as this machine's baseline
          check    render every scene and compare it with the baseline; exit 1 if any differs

          --totk-romfs <dir>   (or WRS_TOTK_ROMFS)    the Tears of the Kingdom romfs
          --botw-romfs <dir>   (or WRS_BOTW_ROMFS)    the Breath of the Wild romfs
          --totk-cache <dir>   (or WRS_TOTK_CACHE)    the prepared-model cache for TotK scenes
          --botw-cache <dir>   (or WRS_BOTW_CACHE)    the prepared-model cache for BotW scenes
          --scenes a,b         only these scenes
          --size <px>          image size, default 640
          --golden <dir>       baseline images, default golden/ beside the sources
          --out <dir>          this run's images and difference images, default out/ beside the sources
          --mean <x> --over16 <x>   the most a scene may differ: mean per-channel difference out of 255, and share of visibly different pixels
        """;

    public static RunSettings Parse(string[] args, string projectDirectory)
    {
        if (args.Length == 0 || args[0] is not ("record" or "check"))
            throw new ArgumentException("The first argument must be 'record' or 'check'.");

        var options = new Dictionary<string, string>();
        for (int i = 1; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--") || i + 1 >= args.Length)
                throw new ArgumentException($"Expected '--name value', got '{args[i]}'.");
            options[args[i][2..]] = args[i + 1];
        }

        string? From(string option, string variable) => options.GetValueOrDefault(option) ?? Environment.GetEnvironmentVariable(variable);
        var tolerance = DiffStats.DefaultTolerance;
        return new RunSettings(args[0], From("totk-romfs", "WRS_TOTK_ROMFS"), From("botw-romfs", "WRS_BOTW_ROMFS"),
            From("totk-cache", "WRS_TOTK_CACHE"), From("botw-cache", "WRS_BOTW_CACHE"), options.GetValueOrDefault("scenes"),
            int.Parse(options.GetValueOrDefault("size", "640")),
            Path.GetFullPath(options.GetValueOrDefault("golden") ?? Path.Combine(projectDirectory, "golden")),
            Path.GetFullPath(options.GetValueOrDefault("out") ?? Path.Combine(projectDirectory, "out")),
            new DiffStats(double.Parse(options.GetValueOrDefault("mean") ?? tolerance.MeanAbs.ToString()),
                double.Parse(options.GetValueOrDefault("over16") ?? tolerance.FractionOver16.ToString())));
    }

    public string? RomfsFor(string game) => game == "botw" ? BotwRomfs : TotkRomfs;

    public string? CacheFor(string game) => game == "botw" ? BotwCache : TotkCache;
}
