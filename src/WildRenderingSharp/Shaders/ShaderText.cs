namespace WildRenderingSharp.Shaders;

/// <summary>Text edits for shader source.</summary>
internal static class ShaderText
{
    // Replaces every anchor, and throws when the source lacks it, so a patch cannot silently miss.
    public static string ReplaceRequired(this string source, string anchor, string replacement) =>
        source.Contains(anchor, StringComparison.Ordinal)
            ? source.Replace(anchor, replacement, StringComparison.Ordinal)
            : throw new InvalidOperationException($"Shader patch anchor not found: {anchor}");
}
