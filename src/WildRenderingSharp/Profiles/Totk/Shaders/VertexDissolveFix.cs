using System.Text.RegularExpressions;

namespace WildRenderingSharp.Profiles.Totk.Shaders;

/// <summary>Leaves a surface whole where its vertex colour says it is whole, in the programs that cut a dissolve out of it with a noise texture.</summary>
/// <remarks>
/// Those programs discard where <c>(vertex colour - noise ^ p - offset) * scale</c> falls below the alpha-test reference. A vertex colour of 1 is the dissolve's
/// painted "intact" end, but the noise reaches 0.97 and the cut lands on its brightest ridges there, as thin crescents on surfaces the game draws unbroken, so the
/// cut is only made where the colour is below that end. The pieces are found by shape, since the temporaries renumber with every extraction.
/// </remarks>
internal static class VertexDissolveFix
{
    // The painted colour at and above which a vertex is whole.
    const string Intact = "0.99";

    static readonly Regex NegatedOffset = new(@"(?<offset>temp_\d+) = 0\.0 - fp_c11\.data\[30\]\.z;", RegexOptions.Compiled);
    static readonly Regex Test = new(@"(?<test>temp_\d+) = temp_\d+ < fp_c11\.data\[33\]\.z;", RegexOptions.Compiled);

    public static string Apply(string source)
    {
        if (!source.Contains("fp_c11.data[30].z", StringComparison.Ordinal))
            return source;

        var offset = NegatedOffset.Match(source);
        var test = Test.Match(source);
        if (!offset.Success || !test.Success)
            return source;

        // temp_T = temp_R + temp_offset, where temp_R = temp_x + temp_colour, and the colour is a vertex attribute's first component.
        var sum = Regex.Match(source, $@"temp_\d+ = (?<r>temp_\d+) \+ {offset.Groups["offset"].Value};");
        if (!sum.Success)
            return source;
        var term = Regex.Match(source, $@"{sum.Groups["r"].Value} = temp_\d+ \+ (?<colour>temp_\d+);");
        if (!term.Success)
            return source;
        var attribute = Regex.Match(source, $@"{term.Groups["colour"].Value} = (?<attribute>in_attr\d+\.x);");
        if (!attribute.Success)
            return source;

        string condition = test.Groups["test"].Value;
        var discard = Regex.Match(source[test.Index..], $@"if \({condition}\)");
        if (!discard.Success)
            return source;

        int at = test.Index + discard.Index;
        return source[..at] + $"if ({condition} && {attribute.Groups["attribute"].Value} < {Intact})" + source[(at + discard.Length)..];
    }
}
