using System.Text.RegularExpressions;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Shaders;

/// <summary>The shared decompiled-GLSL cleanup, plus a unit of its own for each vertex texture TotK's engine renders itself.</summary>
public static partial class TotkGlsl
{
    public const int WindSwellUnit = 32, LieMapUnit = 33, ThicknessUnit = 34;

    // Each shader numbers these onto units where the resolve leaves G-buffer attachments bound, so foliage would read last frame's G-buffer.
    [GeneratedRegex(@"layout\s*\(\s*binding\s*=\s*\d+\s*\)\s*uniform\s+sampler2D\s+(c\d+_(TexWindSwell|TexLieMap|TexThickness))\s*;")]
    private static partial Regex EngineVertexTexture();

    public static string Clean(string source) =>
        EngineVertexTexture().Replace(DecompiledGlsl.Clean(source, TotkBindings.Orphan),
            m => $"layout (binding = {UnitOf(m.Groups[2].Value)}) uniform sampler2D {m.Groups[1].Value};");

    static int UnitOf(string texture) => texture switch
    {
        "TexWindSwell" => WindSwellUnit,
        "TexLieMap" => LieMapUnit,
        _ => ThicknessUnit,
    };
}
