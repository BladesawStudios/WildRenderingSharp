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

        return string.Join('\n', output);
    }
}
