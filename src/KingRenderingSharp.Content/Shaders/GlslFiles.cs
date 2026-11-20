using System.Reflection;

namespace KingRenderingSharp.Content.Shaders;

/// <summary>The renderer's own GLSL, shipped as embedded files under <c>Glsl/</c> in the layer that uses them. Game shaders never live here; they come from the prepared cache.</summary>
internal static class GlslFiles
{
    public static string Load(string path)
    {
        string wanted = "Glsl/" + path;
        foreach (var (assembly, name) in LayerResources.Named())
        {
            if (name.Replace(Path.DirectorySeparatorChar, '/') != wanted)
                continue;
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            string text = reader.ReadToEnd().ReplaceLineEndings("\n");
            return text.EndsWith('\n') ? text[..^1] : text;
        }

        throw new FileNotFoundException($"No embedded GLSL file '{path}'.");
    }
}
