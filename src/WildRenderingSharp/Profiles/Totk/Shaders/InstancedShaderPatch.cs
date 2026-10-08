using WildRenderingSharp.Graphics;
using System.Text;
using System.Text.RegularExpressions;
using WildRenderingSharp.Pipeline;

namespace WildRenderingSharp.Profiles.Totk.Shaders;

/// <summary>Makes a decompiled game vertex shader draw many placements in one instanced call, without changing anything it computes.</summary>
public static class InstancedShaderPatch
{
    static readonly Regex MainSignature = new(@"\bvoid\s+main\s*\(\s*\)", RegexOptions.Compiled);

    static Regex BlockDeclaration(string blockName) => new(
        @"layout\s*\([^)]*\)\s*uniform\s+" + Regex.Escape(blockName) + @"\s*\{[^}]*\}\s*(\w+)\s*;",
        RegexOptions.Compiled);

    public static string? Apply(string vertexSource)
    {
        string source = vertexSource;
        source = RemoveBlock(source, "_ShpMtx", out string? shapeName);
        source = RemoveBlock(source, "_Mtx", out string? paletteName);

        if (shapeName is not null)
            source = RedirectReads(source, shapeName, "wrs_shp");
        if (paletteName is not null)
            source = RedirectReads(source, paletteName, "wrs_mtx");

        Match main = MainSignature.Match(source);
        if (!main.Success)
            return null;
        source = source[..main.Index] + "void wrs_inner_main()" + source[(main.Index + main.Length)..];

        // The helpers depend on nothing in the shader, so they go first - straight after the
        // #version/#extension lines that must open it - where every function that reads the
        // blocks, not only main, comes after them.
        int at = EndOfDirectives(source);
        string helpers = InstancingContract.BaseInstance ? "#extension GL_ARB_shader_draw_parameters : require\n" + Helpers : Helpers;
        source = source[..at] + helpers + source[at..];
        string wrapper = InstancingContract.BaseInstance ? Wrapper.Replace("wrs_first_instance + gl_InstanceID", "wrs_first_instance + gl_BaseInstanceARB + gl_InstanceID") : Wrapper;
        return source.TrimEnd() + "\n\n" + wrapper;
    }

    static int EndOfDirectives(string source)
    {
        int pos = 0;
        while (pos < source.Length)
        {
            int eol = source.IndexOf('\n', pos);
            int next = eol < 0 ? source.Length : eol + 1;
            string line = source[pos..(eol < 0 ? source.Length : eol)].Trim();
            if (line.Length > 0 && !line.StartsWith('#') && !line.StartsWith("//", StringComparison.Ordinal))
                return pos;
            pos = next;
        }
        return source.Length;
    }

    static readonly string Helpers = GlslFiles.Load("Totk/Shaders/InstancedShaderPatch/Helpers.glsl");

    static readonly string Wrapper = GlslFiles.Load("Totk/Shaders/InstancedShaderPatch/Wrapper.glsl");

    static string RemoveBlock(string source, string blockName, out string? instanceName)
    {
        Match m = BlockDeclaration(blockName).Match(source);
        if (!m.Success)
        {
            instanceName = null;
            return source;
        }
        instanceName = m.Groups[1].Value;
        return source.Remove(m.Index, m.Length);
    }

    static string RedirectReads(string source, string instanceName, string function)
    {
        string token = instanceName + ".data[";
        var sb = new StringBuilder(source.Length);
        int i = 0;
        while (true)
        {
            int at = source.IndexOf(token, i, StringComparison.Ordinal);
            if (at < 0)
                break;

            // Only a whole identifier: vp_c7 must not match inside vp_c17.
            if (at > 0 && (char.IsLetterOrDigit(source[at - 1]) || source[at - 1] == '_'))
            {
                sb.Append(source, i, at + token.Length - i);
                i = at + token.Length;
                continue;
            }

            int open = at + token.Length - 1;
            int close = MatchingBracket(source, open);
            if (close < 0)
                throw new InvalidOperationException($"Unbalanced brackets after '{token}' in a decompiled shader.");

            sb.Append(source, i, at - i);
            sb.Append(function).Append("(int(").Append(source, open + 1, close - open - 1).Append("))");
            i = close + 1;
        }
        sb.Append(source, i, source.Length - i);
        return sb.ToString();
    }

    static int MatchingBracket(string s, int open)
    {
        int depth = 0;
        for (int k = open; k < s.Length; k++)
        {
            if (s[k] == '[') depth++;
            else if (s[k] == ']' && --depth == 0) return k;
        }
        return -1;
    }
}
