using System.Text.Json;

namespace WildRenderingSharp.RenderRegression;

/// <summary>One scene to render: the game, the actor, and the bench options that frame and light it.</summary>
sealed record Scene(string Name, string Game, string Actor, Dictionary<string, string> Options);

/// <summary>The scenes the regression run renders, read from <c>scenes.json</c> beside the sources.</summary>
static class SceneCatalog
{
    sealed record Catalog(List<Scene> Scenes);

    public static IReadOnlyList<Scene> Load(string path)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<Catalog>(File.ReadAllText(path), options)?.Scenes
            ?? throw new InvalidDataException($"'{path}' holds no scenes.");
    }

    public static IReadOnlyList<Scene> Select(IReadOnlyList<Scene> scenes, string? only)
    {
        if (only is null)
            return scenes;
        var chosen = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unknown = chosen.Except(scenes.Select(s => s.Name)).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"No such scene: {string.Join(", ", unknown)}.");
        return scenes.Where(s => chosen.Contains(s.Name)).ToList();
    }
}
