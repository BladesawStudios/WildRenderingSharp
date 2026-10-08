using System.Reflection;

namespace WildRenderingSharp.Graphics;

/// <summary>The renderer's own GLSL, shipped as embedded files under <c>Glsl/</c>. Game shaders never live here; they come from the prepared cache.</summary>
public static class GlslFiles
{
    static readonly Assembly Assembly = typeof(GlslFiles).Assembly;

    static readonly Dictionary<string, string> Resources = Assembly.GetManifestResourceNames()
        .Where(n => n.Replace(Path.DirectorySeparatorChar, '/').StartsWith("Glsl/", StringComparison.Ordinal))
        .ToDictionary(n => n.Replace(Path.DirectorySeparatorChar, '/')["Glsl/".Length..]);

    public static string Load(string path)
    {
        if (!Resources.TryGetValue(path, out var resource))
            throw new FileNotFoundException($"No embedded GLSL file '{path}'.");

        using var stream = Assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        string text = reader.ReadToEnd().ReplaceLineEndings("\n");
        return text.EndsWith('\n') ? text[..^1] : text;
    }
}
