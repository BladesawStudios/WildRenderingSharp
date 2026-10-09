using System.Text.RegularExpressions;

namespace WildRenderingSharp.Tests;

// Each namespace may use only the ones at or below its own layer, so the dependencies of the library point one way.
public partial class LayeringTests
{
    // Lowest first. The root namespace, which holds the renderer itself, sits above all of them.
    static readonly (string Namespace, int Layer)[] Layers =
    [
        ("Storage", 0), ("Rom", 0), ("Imaging", 0), ("Logging", 0),
        ("Gpu", 1),
        ("Rendering", 2), ("Assets.Manifests", 2),
        ("Animation", 3),
        ("Graphics", 4),
        ("Shaders", 5),
        ("Assets", 6),
        ("Debug", 7), ("Pipeline", 7),
        ("Profiles", 8),
        ("Scene", 9),
        ("Hosting", 10),
    ];

    const int RootLayer = 11;

    [GeneratedRegex(@"^namespace WildRenderingSharp\.?([\w.]*);", RegexOptions.Multiline)]
    private static partial Regex NamespaceLine();

    [GeneratedRegex(@"^using WildRenderingSharp\.?([\w.]*);", RegexOptions.Multiline)]
    private static partial Regex UsingLine();

    [Fact]
    public void NoNamespaceUsesOneAboveItsLayer()
    {
        var violations = new List<string>();
        foreach (string file in Directory.EnumerateFiles(LibrarySourceDirectory(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;
            string text = File.ReadAllText(file);
            if (NamespaceLine().Match(text) is not { Success: true } own)
                continue;

            int layer = LayerOf(own.Groups[1].Value);
            foreach (Match use in UsingLine().Matches(text))
            {
                if (LayerOf(use.Groups[1].Value) > layer)
                    violations.Add($"{Path.GetRelativePath(LibrarySourceDirectory(), file)} uses {use.Groups[1].Value}");
            }
        }

        Assert.Empty(violations);
    }

    static int LayerOf(string dottedNamespace)
    {
        var best = ("", RootLayer);
        foreach (var (name, layer) in Layers)
        {
            bool within = dottedNamespace == name || dottedNamespace.StartsWith(name + ".", StringComparison.Ordinal);
            if (within && name.Length > best.Item1.Length)
                best = (name, layer);
        }
        return best.Item2;
    }

    static string LibrarySourceDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "src", "WildRenderingSharp");
            if (Directory.Exists(candidate))
                return candidate;
        }
        throw new DirectoryNotFoundException("src/WildRenderingSharp was not found above the test assembly.");
    }
}
