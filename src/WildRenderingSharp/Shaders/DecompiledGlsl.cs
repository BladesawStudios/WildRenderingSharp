using System.Text.RegularExpressions;

namespace WildRenderingSharp.Shaders;

/// <summary>Makes the decompiler's GLSL link on desktop GL. The fixes are the same for every game it is run on.</summary>
internal static partial class DecompiledGlsl
{
    static readonly string[] DroppedLinePrefixes =
    [
        "#extension GL_ARB_gpu_shader_int64", "#extension GL_ARB_shader_ballot",
        "#extension GL_ARB_shader_group_vote", "#extension GL_EXT_shader_image_load_formatted",
        "#extension GL_EXT_texture_shadow_lod", "#extension GL_ARB_fragment_shader_interlock",
        "#extension GL_NV_viewport_array2", "#extension GL_ARB_shader_draw_parameters",
        "#extension GL_ARB_shader_viewport_layer_array", "#pragma optionNV",
    ];

    // exp2(log2(0) * k) is NaN, and a pow() base that is zero for want of a decoded uniform would spread it through the frame.
    static readonly string[] SafeMathDefines =
    [
        "#define safe_log2(x) log2(max(float(x), 1e-7))",
        "#define log2(x) safe_log2(x)",
        "#define safe_inversesqrt(x) inversesqrt(max(float(x), 1e-12))",
        "#define inversesqrt(x) safe_inversesqrt(x)",
    ];

    // Matches a negative layout binding such as `binding = -1`: the decompiler numbers constant buffer N as binding N - 3, so the driver's own
    // buffer comes out negative. Stricter drivers refuse it, and nothing fills that buffer, so it can share one binding.
    [GeneratedRegex(@"binding\s*=\s*-\d+")]
    private static partial Regex NegativeBinding();

    // Matches a whole line that assigns into a fragment storage buffer, such as `fp_s0.data[3] = x;`, but not a comparison. Nothing here reads
    // the engine's feedback buffer, and a shader with side effects loses early depth testing.
    [GeneratedRegex(@"^[ \t]*fp_s\d+\.data\[[^\]\n]*\][ \t]*=(?!=)[^;\n]*;", RegexOptions.Multiline)]
    private static partial Regex FragmentStorageWrite();

    // Buffers that come out of the decompiler with a negative binding are moved to orphanBinding.
    public static string Clean(string source, uint orphanBinding)
    {
        var output = new List<string>();
        bool versionSeen = false;

        foreach (string rawLine in source.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (Array.Exists(DroppedLinePrefixes, p => line.TrimStart().StartsWith(p, StringComparison.Ordinal)))
                continue;

            output.Add(FixFragmentOutput(line));
            if (!versionSeen && line.TrimStart().StartsWith("#version", StringComparison.Ordinal))
            {
                versionSeen = true;
                output.AddRange(SafeMathDefines);
            }
        }

        string cleaned = NegativeBinding().Replace(string.Join('\n', output), $"binding = {orphanBinding}");
        return ShadowLodRewrite.Apply(FragmentStorageWrite().Replace(cleaned, ""));
    }

    // A lone fragment output comes out as an array, which desktop GLSL rejects; an output needs a location and no brackets.
    static string FixFragmentOutput(string line)
    {
        if (line.Contains("out vec4 output_color[0];"))
            line = "layout (location = 0) out vec4 output_color0;";
        return line.Replace("output_color[0]", "output_color0");
    }
}
