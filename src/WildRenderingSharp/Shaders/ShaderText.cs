namespace WildRenderingSharp.Shaders;

public static class ShaderText
{
    /// <summary>Replaces every <paramref name="anchor"/>, and throws when the source lacks it, so a patch cannot silently miss.</summary>
    public static string ReplaceRequired(this string source, string anchor, string replacement) =>
        source.Contains(anchor, StringComparison.Ordinal)
            ? source.Replace(anchor, replacement, StringComparison.Ordinal)
            : throw new InvalidOperationException($"Shader patch anchor not found: {anchor}");
}
