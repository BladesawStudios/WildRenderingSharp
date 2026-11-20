using System.Reflection;

namespace KingRenderingSharp.Content.Shaders;

/// <summary>Finds the embedded files of every KingRenderingSharp layer, because the renderer's GLSL and shader patch sources are spread over the projects that own them.</summary>
internal static class LayerResources
{
    static readonly string[] LayerNames =
    [
        "KingRenderingSharp.Content", "KingRenderingSharp.Graph", "KingRenderingSharp.Games.Botw", "KingRenderingSharp.Games.Totk",
    ];

    // A layer is loaded by name, so the set of files (and the revision made from them) is the same whatever the host has touched so far.
    static readonly Lazy<Assembly[]> Loaded = new(() => LayerNames.Select(Load).OfType<Assembly>().ToArray());

    public static IEnumerable<Assembly> Layers() => Loaded.Value;

    public static IEnumerable<(Assembly Assembly, string Name)> Named() =>
        Layers().SelectMany(a => a.GetManifestResourceNames().Select(n => (a, n)));

    // A host that leaves a game out of its build has no such assembly, and then has no files of that game to find.
    static Assembly? Load(string name)
    {
        try
        {
            return Assembly.Load(new AssemblyName(name));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
