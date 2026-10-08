using System.Text;
using System.Text.RegularExpressions;

namespace WildRenderingSharp.Profiles.Totk.Shaders;

/// <summary>Cleans one of <c>ShaderLibrary.CompileTool</c>'s decompiled <c>.vert</c>/<c>.frag</c> files so desktop GL will link it.</summary>
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

            // The decompiler emits a single fragment output as an array ("out vec4 output_color[0];"), which desktop GLSL rejects; a real output needs an explicit location and no brackets.
            if (line.Contains("out vec4 output_color[0];"))
                line = "layout (location = 0) out vec4 output_color0;";
            line = line.Replace("output_color[0]", "output_color0");

            output.Add(line);

            if (!versionFound && line.TrimStart().StartsWith("#version", StringComparison.Ordinal))
            {
                versionFound = true;
                // Uniforms the console guarantees nonzero as pow() exponents (see EnvUbo.PowExponentSlots) can be zero here, where still-undecoded slots are reconstructed, and the compiler's pow(x,k)
                // idiom turns exp2(log2(0) * k) into NaN. Guarding log2 and inversesqrt is cheap insurance against it spreading through the frame.
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

    // A fragment stage's writes to a storage buffer (fp_s0.data[N] = 2u;), in a quarter of the material programs and the
    // deferred passes. They are the engine's feedback (which materials were seen), read by nothing here, but a fragment shader
    // with a side effect cannot be depth-tested early, so the Z-only prepass saved nothing for those materials and millions of
    // fragments a frame wrote the same few words: most of a 33 ms G-buffer pass. Binding 0 is also where instanced draws keep
    // their bake table.
    static readonly Regex FragmentStorageWrite = new(@"^[ \t]*fp_s\d+\.data\[[^\]\n]*\][ \t]*=(?!=)[^;\n]*;", RegexOptions.Compiled | RegexOptions.Multiline);

    // Textures the engine renders at runtime and foliage vertex shaders sample, which nothing in romfs supplies: TexWindSwell,
    // TexLieMap (where grass is pressed flat) and TexThickness. Each shader numbers them its own way, and those units are where
    // the resolve leaves G-buffer attachments bound, so foliage was bent by last frame's G-buffer (the wind droop term cubes it
    // into spikes). They move to units of their own, where the pipeline keeps each one's neutral bound.
    static readonly Regex EngineVertexTexture = new(
        @"layout\s*\(\s*binding\s*=\s*\d+\s*\)\s*uniform\s+sampler2D\s+(c\d+_(TexWindSwell|TexLieMap|TexThickness))\s*;", RegexOptions.Compiled);

    public const int WindSwellUnit = 32, LieMapUnit = 33, ThicknessUnit = 34;

    static int EngineVertexTextureUnit(string name) => name switch
    {
        "TexWindSwell" => WindSwellUnit,
        "TexLieMap" => LieMapUnit,
        _ => ThicknessUnit,
    };

    public const uint OrphanBlockBinding = Profiles.Totk.TotkBindings.Orphan;

    // The decompiler renumbers constant buffer N to binding N - 3, so the driver's own buffer (c0) comes out as binding = -3.
    // NVIDIA lets that through, a stricter driver refuses the shader; nothing ever supplied that buffer, so its one read has
    // always been of nothing.
    static readonly Regex NegativeBinding = new(@"binding\s*=\s*-\d+", RegexOptions.Compiled);

    static readonly Regex ShadowSamplerDeclaration = new(
        @"\buniform\s+(sampler2DArrayShadow|samplerCubeShadow|samplerCubeArrayShadow)\s+(\w+)\s*;", RegexOptions.Compiled);

    // textureLod on an array-shadow or cube-shadow sampler exists only through GL_EXT_texture_shadow_lod, which the decompiler
    // asks for and this class strips. NVIDIA accepts the call anyway; Intel and AMD refuse the whole shader ("no matching
    // overloaded function"). Core GLSL's textureGrad with zero derivatives takes the same coordinate and samples the same base
    // level (a shadow map has only one), so the call is rewritten to that.
    static string ShadowLodToGrad(string source)
    {
        foreach (Match decl in ShadowSamplerDeclaration.Matches(source))
        {
            string zero = decl.Groups[1].Value == "sampler2DArrayShadow" ? "vec2(0.0)" : "vec3(0.0)";
            source = RewriteCalls(source, decl.Groups[2].Value, zero);
        }
        return source;
    }

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
