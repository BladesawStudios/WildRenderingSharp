using System.Globalization;
using System.Text.RegularExpressions;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>
/// The material IDs a G-buffer program writes into attachment 0's red byte, read from its decompiled source. The game draws each resolve pass where
/// that byte equals its priority, so the shader and not the material's name decides which pass lights a pixel.
/// </summary>
public static partial class GBufferMaterialIds
{
    public static IReadOnlyList<int> Parse(string fragmentSource)
    {
        Match output = OutputRegex().Match(fragmentSource);
        if (!output.Success)
            return [];

        string expression = output.Groups[1].Value.Trim();
        var values = new SortedSet<int>();
        if (!AddConstants(expression, values))
        {
            // intBitsToFloat(temp_N) or temp_N: every assignment to the temp has to be a constant too.
            Match temp = TempRegex().Match(expression);
            if (!temp.Success)
                return [];
            bool any = false;
            foreach (Match assignment in Regex.Matches(fragmentSource, @"\b" + temp.Groups[1].Value + @" = (.*?);"))
            {
                any = true;
                if (!AddConstants(assignment.Groups[1].Value, values))
                    return [];
            }
            if (!any)
                return [];
        }
        values.Remove(0);
        return [.. values];
    }

    // Adds every constant in the expression (a literal, a hex bit pattern or a ternary of them); false when it holds anything else.
    static bool AddConstants(string expression, SortedSet<int> into)
    {
        var found = new List<float>();
        string rest = BitsOfFloatRegex().Replace(expression, m =>
        {
            found.Add(float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            return " ";
        });
        rest = HexRegex().Replace(rest, m =>
        {
            found.Add(BitConverter.Int32BitsToSingle((int)uint.Parse(m.Groups[1].Value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
            return " ";
        });
        rest = FloatRegex().Replace(rest, m =>
        {
            found.Add(float.Parse(m.Value, CultureInfo.InvariantCulture));
            return " ";
        });

        // What remains may only be the ternary's punctuation and the name of its condition.
        string leftover = ConditionRegex().Replace(rest, " ").Replace("intBitsToFloat", " ");
        foreach (char punctuation in "()?:")
            leftover = leftover.Replace(punctuation, ' ');
        if (!string.IsNullOrWhiteSpace(leftover) || found.Count == 0)
            return false;
        foreach (float value in found)
            into.Add((int)MathF.Round(value * 255f));
        return true;
    }

    [GeneratedRegex(@"output_color\[0\]\.x = (.*?);")]
    private static partial Regex OutputRegex();

    [GeneratedRegex(@"^(?:intBitsToFloat\()?(temp_\d+)\)?$")]
    private static partial Regex TempRegex();

    [GeneratedRegex(@"floatBitsToInt\(\s*([-+]?\d+\.?\d*(?:[eE][-+]?\d+)?)\s*\)")]
    private static partial Regex BitsOfFloatRegex();

    [GeneratedRegex(@"\b(0x[0-9A-Fa-f]+)\b")]
    private static partial Regex HexRegex();

    [GeneratedRegex(@"(?<![\w.])[-+]?\d+\.\d*(?:[eE][-+]?\d+)?(?![\w.])")]
    private static partial Regex FloatRegex();

    [GeneratedRegex(@"!?\btemp_\d+\b")]
    private static partial Regex ConditionRegex();
}
