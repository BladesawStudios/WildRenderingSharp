using System.Text.RegularExpressions;

namespace WildRenderingSharp.Profiles.Totk.Shaders;

/// <summary>Moves a game program's gather of its own pixel's depth a quarter texel in, so the gather cannot anchor on the neighbouring texel.</summary>
/// <remarks>
/// A pass whose target is the sampled texture's size gathers at its pixel's centre, which is exactly where the gather's four-texel footprint changes. Float error
/// in the interpolated coordinate then decides, row by row and column by column, whether the footprint anchors on the pixel's own texel or the one before it: the
/// rows that miss show as bands and the columns as vertical lines. A quarter texel in keeps the footprint on the texel's own side. A gather of a texture of
/// another size is left alone, since its footprint does not change at the pixel's centre.
/// </remarks>
internal static class PixelCentreGather
{
    public static string Anchor(string fragment, string sampler) =>
        Regex.Replace(fragment, $@"textureGather\({Regex.Escape(sampler)}, vec2\((temp_\d+), (temp_\d+)\), 0\)", match =>
        {
            string u = match.Groups[1].Value, v = match.Groups[2].Value;
            return ReadsPixelCentre(fragment, u, v)
                ? $"textureGather({sampler}, vec2({u}, {v}) + 0.25 / vec2(textureSize({sampler}, 0)), 0)"
                : match.Value;
        });

    // Whether the two temporaries are the pixel's own coordinate, as the fullscreen quad's first interpolant gives it.
    static bool ReadsPixelCentre(string fragment, string u, string v) =>
        Regex.IsMatch(fragment, $@"^\s*{u} = in_attr0\.x;", RegexOptions.Multiline) && Regex.IsMatch(fragment, $@"^\s*{v} = in_attr0\.y;", RegexOptions.Multiline);
}
