using System.Text;
using System.Text.RegularExpressions;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Makes a decompiled game vertex shader draw many placements in one instanced call, without
/// changing anything it computes.
/// </summary>
/// <remarks>
/// <para>
/// The game issues one draw per placed model shape: each draw rebinds the shape's own
/// <c>ShpMtx</c> (binding 4, its placement) and <c>_Mtx</c> (binding 2, its bone palette) and the
/// vertex shader reads them at fixed or computed indices - confirmed in the binary, where gsys has
/// no instancing path for material shaders at all. NVN makes that cheap; desktop GL does not, and a
/// map section is fifteen thousand placements.
/// </para>
/// <para>
/// So the only thing this changes is where those two blocks' values come from. Each block's
/// declaration is removed and every <c>NAME.data[expr]</c> becomes a call returning the same vec4
/// for this instance, out of one storage buffer holding every placement's ShpMtx rows and bone
/// palette back to back (<see cref="InstanceBatch"/>). The decompiled <c>main</c> is renamed and a
/// new <c>main</c> works out this instance's base before calling it - the same wrapper technique
/// <see cref="CloudDistanceFade"/> uses - so every original instruction runs unmodified, in order,
/// on the same values the per-actor path feeds it.
/// </para>
/// <para>
/// What a read past the end returns is reproduced too: <c>ShpMtx</c> holds three rows and zeros
/// after them - except row 8, the instance's baked-lighting table entry, which each instance
/// carries (<see cref="InstanceBatch.SetBake"/>) - and a bone palette is filled past its last bone with identity matrices - or, for a
/// model with no skeleton, with its placement in every slot (<see cref="Shaders.Profiles.Totk.Ubos.BonePaletteUbo"/>).
/// Fragment shaders are left alone: the two that declare <c>ShpMtx</c> read only a reserved,
/// always-zero row, which the zero block bound alongside instanced draws still supplies.
/// </para>
/// </remarks>
public static class InstancedShaderPatch
{
    /// <summary>The storage-buffer binding the instance data is read from. The decompiled shaders themselves use binding 0.</summary>
    public const int InstanceBinding = 7;

    public const string FirstInstanceUniform = "wrs_first_instance";
    public const string StrideUniform = "wrs_instance_stride";
    public const string PaletteVec4sUniform = "wrs_palette_vec4s";
    public const string PaletteRepeatUniform = "wrs_palette_repeat";

    /// <summary>
    /// Whether instances are found through <c>gl_BaseInstance</c> (ARB_shader_draw_parameters) as
    /// well as the first-instance uniform - which is what lets every visible run of a shape go out
    /// in one multi-draw (see <see cref="ShapeDrawing.DrawInstanced"/>). Set once a context is
    /// known to have it, before any instanced program is built.
    /// </summary>
    public static bool BaseInstance { get; set; }

    static readonly Regex MainSignature = new(@"\bvoid\s+main\s*\(\s*\)", RegexOptions.Compiled);

    static Regex BlockDeclaration(string blockName) => new(
        @"layout\s*\([^)]*\)\s*uniform\s+" + Regex.Escape(blockName) + @"\s*\{[^}]*\}\s*(\w+)\s*;",
        RegexOptions.Compiled);

    /// <summary>Patches a decompiled vertex shader. Returns null when it has no <c>main</c> to wrap, which no real one lacks.</summary>
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
        string helpers = BaseInstance ? "#extension GL_ARB_shader_draw_parameters : require\n" + Helpers : Helpers;
        source = source[..at] + helpers + source[at..];
        string wrapper = BaseInstance ? Wrapper.Replace("wrs_first_instance + gl_InstanceID", "wrs_first_instance + gl_BaseInstanceARB + gl_InstanceID") : Wrapper;
        return source.TrimEnd() + "\n\n" + wrapper;
    }

    /// <summary>Where the opening run of preprocessor directives, blank lines and comments ends.</summary>
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

    const string Helpers = """
        // ---- WildRenderingSharp instancing (InstancedShaderPatch) ----
        layout (binding = 7, std430) readonly buffer _WrsInstances { vec4 wrs_inst[]; };
        uniform int wrs_first_instance;
        uniform int wrs_instance_stride;
        uniform int wrs_palette_vec4s;
        uniform int wrs_palette_repeat;
        int wrs_base;

        vec4 wrs_shp(int i)
        {
            if (i >= 0 && i < 3) return wrs_inst[wrs_base + i];
            if (i == 8) return wrs_inst[wrs_base + 3];
            return vec4(0.0);
        }

        vec4 wrs_mtx(int i)
        {
            int row = ((i % 3) + 3) % 3;
            if (i >= 0 && i < wrs_palette_vec4s)
                return wrs_inst[wrs_base + 4 + i];
            if (wrs_palette_repeat != 0)
                return wrs_inst[wrs_base + 4 + row];
            return vec4(row == 0 ? 1.0 : 0.0, row == 1 ? 1.0 : 0.0, row == 2 ? 1.0 : 0.0, 0.0);
        }
        // ---- end instancing ----


        """;

    const string Wrapper = """
        void main()
        {
            wrs_base = (wrs_first_instance + gl_InstanceID) * wrs_instance_stride;
            wrs_inner_main();
        }

        """;

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

    /// <summary>Rewrites every <c>name.data[expr]</c> to <c>function(int(expr))</c>, matching brackets so an index that itself indexes something stays whole.</summary>
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
