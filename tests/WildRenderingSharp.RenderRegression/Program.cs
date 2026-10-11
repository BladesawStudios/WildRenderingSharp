using WildRenderingSharp.RenderRegression;

try
{
    string project = FindProjectDirectory();
    var settings = RunSettings.Parse(args, project);
    var scenes = SceneCatalog.Select(SceneCatalog.Load(Path.Combine(project, "scenes.json")), settings.Only);
    return new RegressionRun(settings, BenchProcess.Locate(project)).Execute(scenes);
}
catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or InvalidDataException)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(RunSettings.Usage);
    return 2;
}

// The folder holding scenes.json, found above the build output.
static string FindProjectDirectory()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "scenes.json")))
            return dir.FullName;
    }
    throw new FileNotFoundException("scenes.json was not found above the build output.");
}
