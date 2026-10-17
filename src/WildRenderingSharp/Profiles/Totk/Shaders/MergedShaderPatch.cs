using System.Text;
using System.Text.RegularExpressions;
using WildRenderingSharp.Pipeline.Merged;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Shaders;

/// <summary>Makes a decompiled game vertex and fragment pair draw any shape of any batch from one multi-draw, reading what the shape owns from storage buffers.</summary>
/// <remarks>
/// The material block, the textures and the instances are looked up from the draw's record, which the vertex stage picks by gl_DrawID and hands to the
/// fragment stage. A pair that reads what this cannot supply comes back null, and is drawn one shape at a time.
/// </remarks>
internal static partial class MergedShaderPatch
{
    static readonly string VertexHelpers = GlslFiles.Load("Totk/Shaders/MergedShaderPatch/VertexHelpers.glsl");
    static readonly string VertexWrapper = GlslFiles.Load("Totk/Shaders/MergedShaderPatch/VertexWrapper.glsl");
    static readonly string FragmentHelpers = GlslFiles.Load("Totk/Shaders/MergedShaderPatch/FragmentHelpers.glsl");

    // A texture the shape owns, which a program declares at a unit of its own.
    [GeneratedRegex(@"layout\s*\(\s*binding\s*=\s*(\d+)\s*\)\s*uniform\s+(sampler\w+)\s+(cTexture\w*)\s*;")]
    private static partial Regex ShapeSampler();

    [GeneratedRegex(@"layout\s*\(\s*binding\s*=\s*\d+\s*\)\s*uniform\s+sampler\w+\s+(cVtxTexture\w*)\s*;")]
    private static partial Regex VertexShapeSampler();

    public static (string Vertex, string Fragment)? Apply(string vertexSource, string fragmentSource)
    {
        string? vertex = PatchVertex(vertexSource);
        string? fragment = vertex is null ? null : PatchFragment(fragmentSource);
        return vertex is null || fragment is null ? null : (vertex, fragment);
    }

    // The pass-ID stamp's own shaders in the same form, where what the stamp needs of a shape, its skin count and its pass, rides in the draw's record.
    public static (string Vertex, string Fragment) PassId(string vertexSource, string fragmentSource)
    {
        string vertex = vertexSource.Replace("uniform int uSkinCount;", "").Replace("uSkinCount", "wrs_d.shape.w");
        vertex = vertex.Replace("gl_Position = uMVP * vec4(p, 1.0);",
            "vec4 v0 = vec4(p, 1.0); gl_Position = uViewProj * vec4(dot(v0, wrs_shp(0)), dot(v0, wrs_shp(1)), dot(v0, wrs_shp(2)), 1.0);");
        string fragment = fragmentSource.Replace("uniform float uId;", "layout (location = 28) flat in ivec4 wrs_v;").Replace("uId", "(float(wrs_v.x) / 255.0)");
        return (PatchVertex(vertex) ?? throw new InvalidOperationException("The pass-ID stamp's vertex shader does not patch."), fragment);
    }

    static string? PatchVertex(string source)
    {
        if (VertexShapeSampler().IsMatch(source))
            return null;

        source = InstancedShaderPatch.RemoveBlock(source, "_ShpMtx", out string? shapeName);
        source = InstancedShaderPatch.RemoveBlock(source, "_Mtx", out string? paletteName);
        source = InstancedShaderPatch.RemoveBlock(source, "_Mat", out string? materialName);
        if (shapeName is not null) source = InstancedShaderPatch.RedirectReads(source, shapeName, "wrs_shp");
        if (paletteName is not null) source = InstancedShaderPatch.RedirectReads(source, paletteName, "wrs_mtx");
        if (materialName is not null)
        {
            if (!ReadsWithin(source, materialName, MergedLayout.MaterialVec4s))
                return null;
            source = InstancedShaderPatch.RedirectReads(source, materialName, "wrs_mat");
        }
        return Wrap(source, "#extension GL_ARB_shader_draw_parameters : require\n" + VertexHelpers, VertexWrapper);
    }

    static string? PatchFragment(string source)
    {
        source = InstancedShaderPatch.RemoveBlock(source, "_Mat", out string? materialName);
        if (materialName is not null)
        {
            if (!ReadsWithin(source, materialName, MergedLayout.MaterialVec4s))
                return null;
            source = InstancedShaderPatch.RedirectReads(source, materialName, "wrs_fmat");
        }

        var samplers = new List<(string Type, string Name, int Unit)>();
        source = ShapeSampler().Replace(source, m =>
        {
            samplers.Add((m.Groups[2].Value, m.Groups[3].Value, int.Parse(m.Groups[1].Value)));
            return "";
        });
        if (samplers.Any(s => s.Unit >= MergedLayout.HandleSlots))
            return null;

        string variables = string.Concat(samplers.Select(s => $"{s.Type} {s.Name};\n"));
        return Wrap(source, "#extension GL_ARB_bindless_texture : require\n" + FragmentHelpers + "\n" + variables, FragmentWrapper(samplers));
    }

    // The fragment main picks each sampler up from the handle table before anything reads it; the baked-lighting one prefers the atlas its instance named.
    static string FragmentWrapper(IReadOnlyList<(string Type, string Name, int Unit)> samplers)
    {
        var text = new StringBuilder("void main()\n{\n");
        foreach (var (type, name, unit) in samplers)
        {
            string own = $"{type}(wrs_handles[wrs_v.y + {unit}])";
            if (name.StartsWith("cTextureBake", StringComparison.Ordinal))
                text.Append($"    if (wrs_v.z != 0 || wrs_v.w != 0) {name} = {type}(uvec2(uint(wrs_v.z), uint(wrs_v.w)));\n    else {name} = {own};\n");
            else
                text.Append($"    {name} = {own};\n");
        }
        return text.Append("    wrs_inner_main();\n}\n").ToString();
    }

    static string Wrap(string source, string helpers, string wrapper)
    {
        Match main = InstancedShaderPatch.MainSignature.Match(source);
        if (!main.Success)
            throw new InvalidOperationException("A decompiled shader has no main to wrap.");
        source = source[..main.Index] + "void wrs_inner_main()" + source[(main.Index + main.Length)..];
        int at = InstancedShaderPatch.EndOfDirectives(source);
        return source[..at] + helpers + "\n" + source[at..].TrimEnd() + "\n\n" + wrapper;
    }

    // Whether every read of the block is of a fixed vec4 below the limit.
    static bool ReadsWithin(string source, string instanceName, int limit)
    {
        string token = instanceName + ".data[";
        for (int at = source.IndexOf(token, StringComparison.Ordinal); at >= 0; at = source.IndexOf(token, at + token.Length, StringComparison.Ordinal))
        {
            int close = source.IndexOf(']', at);
            if (close < 0 || !int.TryParse(source.AsSpan(at + token.Length, close - at - token.Length).Trim(), out int index) || index >= limit)
                return false;
        }
        return true;
    }
}
