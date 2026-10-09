using System.Text;
using System.Text.RegularExpressions;

namespace WildRenderingSharp.Shaders;

/// <summary>
/// Rewrites <c>textureLod</c> on array and cube shadow samplers, which needs an extension the cleanup strips and which some drivers
/// refuse without it. <c>textureGrad</c> with zero derivatives takes the same coordinate and samples the same level.
/// </summary>
internal static partial class ShadowLodRewrite
{
    [GeneratedRegex(@"\buniform\s+(sampler2DArrayShadow|samplerCubeShadow|samplerCubeArrayShadow)\s+(\w+)\s*;")]
    private static partial Regex ShadowSamplerDeclaration();

    public static string Apply(string source)
    {
        foreach (Match declaration in ShadowSamplerDeclaration().Matches(source))
        {
            string zero = declaration.Groups[1].Value == "sampler2DArrayShadow" ? "vec2(0.0)" : "vec3(0.0)";
            source = RewriteCalls(source, declaration.Groups[2].Value, zero);
        }
        return source;
    }

    static string RewriteCalls(string source, string sampler, string zero)
    {
        var call = new Regex(@"\btextureLod\s*\(\s*" + Regex.Escape(sampler) + @"\s*,");
        var result = new StringBuilder(source.Length);
        int at = 0;
        for (Match match = call.Match(source); match.Success; match = call.Match(source, at))
        {
            int open = source.IndexOf('(', match.Index);
            if (!TryReadArguments(source, open, out var args, out int close) || args.Count != 3)
            {
                result.Append(source, at, match.Index + match.Length - at);
                at = match.Index + match.Length;
                continue;
            }

            result.Append(source, at, match.Index - at);
            result.Append("textureGrad(").Append(args[0].Trim()).Append(", ").Append(args[1].Trim())
                .Append(", ").Append(zero).Append(", ").Append(zero).Append(')');
            at = close + 1;
        }
        result.Append(source, at, source.Length - at);
        return result.ToString();
    }

    // The top-level, comma-separated arguments of the call whose opening parenthesis is at open.
    static bool TryReadArguments(string source, int open, out List<string> args, out int close)
    {
        args = [];
        close = -1;
        int depth = 0, start = open + 1;
        for (int i = open; i < source.Length && close < 0; i++)
        {
            char c = source[i];
            if (c is '(' or '[')
                depth++;
            else if (c is ')' or ']')
            {
                if (--depth == 0)
                {
                    args.Add(source[start..i]);
                    close = i;
                }
            }
            else if (c == ',' && depth == 1)
            {
                args.Add(source[start..i]);
                start = i + 1;
            }
        }
        return close >= 0;
    }
}
