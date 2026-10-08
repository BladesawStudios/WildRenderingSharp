using System.Text;
using System.Text.RegularExpressions;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Cleans up one of <c>ShaderLibrary.CompileTool</c>'s decompiled <c>.vert</c>/<c>.frag</c> files
/// so desktop GL will link it - direct port of <c>render_deferred_master_sword.clean_glsl</c>.
/// The decompiled source targets a Tegra/NVN GLSL dialect that declares several
/// hardware/driver-specific extensions desktop drivers don't have, and the decompiler's own
/// array-output syntax for a single fragment output needs a small rewrite. Never edit the shader
/// logic itself here - only strip/rewrite the handful of things that keep it from *compiling* at
/// all; the shading math is evidence, not editable source.
/// </summary>
public static class GlslSanitizer
{
    static readonly string[] DroppedLinePrefixes =
    [
        "#extension GL_ARB_gpu_shader_int64", "#extension GL_ARB_shader_ballot",
        "#extension GL_ARB_shader_group_vote", "#extension GL_EXT_shader_image_load_formatted",
        "#extension GL_EXT_texture_shadow_lod", "#extension GL_ARB_fragment_shader_interlock",
        "#extension GL_NV_viewport_array2", "#extension GL_ARB_shader_draw_parameters",
        "#extension GL_ARB_shader_viewport_layer_array", "#pragma optionNV",
    ];

    public static string Clean(string source)
    {
        var output = new List<string>();
        bool versionFound = false;

        foreach (var rawLine in source.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (Array.Exists(DroppedLinePrefixes, d => line.TrimStart().StartsWith(d, StringComparison.Ordinal)))
                continue;

            // The decompiler emits a single fragment output as an array declaration
            // ("out vec4 output_color[0];"), which desktop GLSL rejects - a real output needs an
            // explicit location and no array brackets.
            if (line.Contains("out vec4 output_color[0];"))
                line = "layout (location = 0) out vec4 output_color0;";
            line = line.Replace("output_color[0]", "output_color0");

            output.Add(line);

            if (!versionFound && line.TrimStart().StartsWith("#version", StringComparison.Ordinal))
            {
                versionFound = true;
                // Every uniform this shader was ever meant to read as a pow() exponent is
                // guaranteed nonzero on the console (see EnvUbo.PowExponentSlots for why), but
                // WildRenderingSharp's own reconstruction of a handful of still-undecoded slots leaves some at
                // zero - which turns exp2(log2(0) * k) into NaN through the compiler's own
                // pow(x,k) idiom. Guarding log2/inversesqrt here is cheap insurance against that
                // propagating through an entire frame, and matches what the Python bench does.
                output.Add("#define safe_log2(x) log2(max(float(x), 1e-7))");
                output.Add("#define log2(x) safe_log2(x)");
                output.Add("#define safe_inversesqrt(x) inversesqrt(max(float(x), 1e-12))");
                output.Add("#define inversesqrt(x) safe_inversesqrt(x)");
            }
        }

        string cleaned = NegativeBinding.Replace(string.Join('\n', output), $"binding = {OrphanBlockBinding}");
        cleaned = EngineVertexTexture.Replace(cleaned, m =>
            $"layout (binding = {EngineVertexTextureUnit(m.Groups[2].Value)}) uniform sampler2D {m.Groups[1].Value};");
        cleaned = FragmentStorageWrite.Replace(cleaned, "");
        return ShadowLodToGrad(cleaned);
    }

    /// <summary>
    /// A fragment stage's writes to a storage buffer - <c>fp_s0.data[N] = 2u;</c> - in a quarter of
    /// the game's material programs, and in its deferred passes. They are the engine's feedback
    /// (which materials were seen this frame), read back by nothing here. But a fragment shader with
    /// a side effect cannot be depth-tested early, so for those materials the Z-only prepass saved
    /// nothing - every hidden fragment ran the whole program - and millions of fragments a frame
    /// all wrote the same few words: most of a 33 ms G-buffer pass on a card that should take a
    /// few. Binding 0 is also where the instanced draws keep their bake table.
    /// </summary>
    static readonly Regex FragmentStorageWrite = new(@"^[ \t]*fp_s\d+\.data\[[^\]\n]*\][ \t]*=(?!=)[^;\n]*;", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Textures the engine renders at runtime and foliage vertex shaders sample, which nothing in
    /// romfs supplies: <c>TexWindSwell</c> (wind gusts travelling over the field), <c>TexLieMap</c>
    /// (where grass is pressed flat) and <c>TexThickness</c>. Each shader numbers them its own way
    /// (wind swell is unit 1 in some, 2 in others, where the lie map takes 1), and those units are
    /// where the resolve leaves G-buffer attachments bound - so foliage was bent by last frame's
    /// G-buffer, which the leaves' wind droop term cubes into long spikes. Moved to units of their
    /// own, where <see cref="DeferredPipeline"/> keeps each one's neutral bound.
    /// </summary>
    static readonly Regex EngineVertexTexture = new(
        @"layout\s*\(\s*binding\s*=\s*\d+\s*\)\s*uniform\s+sampler2D\s+(c\d+_(TexWindSwell|TexLieMap|TexThickness))\s*;", RegexOptions.Compiled);

    /// <summary>The units <see cref="EngineVertexTexture"/> moves them to - above every unit a game program numbers itself.</summary>
    public const int WindSwellUnit = 32, LieMapUnit = 33, ThicknessUnit = 34;

    static int EngineVertexTextureUnit(string name) => name switch
    {
        "TexWindSwell" => WindSwellUnit,
        "TexLieMap" => LieMapUnit,
        _ => ThicknessUnit,
    };

    /// <summary>
    /// Where a block the decompiler numbered negatively ends up - see <see cref="NegativeBinding"/>.
    /// <see cref="DeferredPipeline"/> keeps a zeroed buffer bound here, so what such a block reads
    /// is defined.
    /// </summary>
    public const uint OrphanBlockBinding = Profiles.Totk.TotkBindings.Orphan;

    /// <summary>
    /// The decompiler renumbers constant buffer N to binding N - 3, so the driver's own buffer
    /// (c0) comes out as <c>binding = -3</c>. NVIDIA lets that through; a stricter driver refuses the
    /// whole shader. Nothing ever supplied that buffer, so its one read has always been of nothing.
    /// </summary>
    static readonly Regex NegativeBinding = new(@"binding\s*=\s*-\d+", RegexOptions.Compiled);

    static readonly Regex ShadowSamplerDeclaration = new(
        @"\buniform\s+(sampler2DArrayShadow|samplerCubeShadow|samplerCubeArrayShadow)\s+(\w+)\s*;", RegexOptions.Compiled);

    /// <summary>
    /// <c>textureLod</c> on an array-shadow or cube-shadow sampler exists only through
    /// <c>GL_EXT_texture_shadow_lod</c> - which the decompiler asks for and this class strips above.
    /// NVIDIA accepts the call regardless; Intel and AMD, integrated graphics included, refuse the
    /// whole shader ("no matching overloaded function"), and the model fails to load. Core GLSL's
    /// <c>textureGrad</c> with zero derivatives takes the same coordinate and samples the same base
    /// level - and a shadow map has nothing but its base level - so the call is rewritten to that.
    /// </summary>
    static string ShadowLodToGrad(string source)
    {
        foreach (Match decl in ShadowSamplerDeclaration.Matches(source))
        {
            string zero = decl.Groups[1].Value == "sampler2DArrayShadow" ? "vec2(0.0)" : "vec3(0.0)";
            source = RewriteCalls(source, decl.Groups[2].Value, zero);
        }
        return source;
    }

    /// <summary>Every <c>textureLod(name, coord, lod)</c> becomes <c>textureGrad(name, coord, zero, zero)</c>, splitting arguments at top-level commas.</summary>
    static string RewriteCalls(string source, string sampler, string zero)
    {
        var call = new Regex(@"\btextureLod\s*\(\s*" + Regex.Escape(sampler) + @"\s*,");
        var sb = new StringBuilder(source.Length);
        int at = 0;
        for (Match m = call.Match(source); m.Success; m = call.Match(source, at))
        {
            int open = source.IndexOf('(', m.Index);
            var args = new List<string>();
            int depth = 0, start = open + 1, close = -1;
            for (int i = open; i < source.Length && close < 0; i++)
            {
                char c = source[i];
                if (c is '(' or '[') depth++;
                else if (c is ')' or ']')
                {
                    if (--depth == 0) { args.Add(source[start..i]); close = i; }
                }
                else if (c == ',' && depth == 1) { args.Add(source[start..i]); start = i + 1; }
            }
            if (close < 0 || args.Count != 3)
            {
                sb.Append(source, at, m.Index + m.Length - at);
                at = m.Index + m.Length;
                continue;
            }
            sb.Append(source, at, m.Index - at);
            sb.Append("textureGrad(").Append(args[0].Trim()).Append(", ").Append(args[1].Trim())
              .Append(", ").Append(zero).Append(", ").Append(zero).Append(')');
            at = close + 1;
        }
        sb.Append(source, at, source.Length - at);
        return sb.ToString();
    }
}
