using System.Text.RegularExpressions;

namespace WildRenderingSharp.Profiles.Totk.Shaders;

/// <summary>Patches one individually verified decompiler corruption.</summary>
internal static class KnownDecompilerCorrections
{
    // Matches the sub-expression that should be the first argument of an outer fma(...), keyed on the structural shape (the literal
    // 25.0/10.0/-10.0 constants), not on temp_N numbers, which renumber on every --prepare run. Captures the two variable names so they are
    // preserved verbatim.
    static readonly Regex CorruptedHeightTerm = new(
        @"(temp_\d+) \* 0\.0 - max\(min\(0\.0 - (temp_\d+), 25\.0\), 10\.0\) \+ -10\.0",
        RegexOptions.Compiled);

    public static string Apply(string source) =>
        CorruptedHeightTerm.Replace(source, "max($1 * 0.0 - max(min(0.0 - $2, 25.0), 10.0) + -10.0, 0.0)");
}
