using System.Numerics;

namespace WildRenderingSharp.Rendering;

/// <summary>
/// Resolves a palette's hemisphere ambient (sky/ground) colours.
///
/// WHERE THIS ACTUALLY COMES FROM IN THE GAME, established by reading the shipped data rather than
/// guessing. The hemisphere ambient is an <c>agl::env</c> object, NOT a ResEnvPalette field - it
/// lives in <c>Env/GameScene.Nin_NX_NVN.genvb.zs</c> (a SARC of AAMP <c>.baglenv</c> files) as a
/// <c>HemisphereLight</c> with <c>SkyColor</c>/<c>GroundColor</c>/<c>Intensity</c>. But the only
/// hemisphere lights authored there are situational - <c>main_env.baglenv</c> has exactly
/// <c>hemi_inner</c> and <c>hemi_cave</c>, both flat greys - so OUTDOORS there is no stored
/// hemisphere colour at all. Outdoor sky ambient is integrated from the sky itself, which the
/// runtime renders per frame from Rayleigh/Mie scattering and WildRenderingSharp does not render.
///
/// So this stays an approximation of that integral. What changed is that it is now driven by the
/// palette's OWN scattering parameters instead of a fixed constant, which is why every palette used
/// to look the same: the sun colour varied per palette but the ambient never did, and the ambient
/// wins because it lights every surface from every direction while the sun only lights the faces
/// pointing at it. A blood-moon palette rendered blue because its ambient was the same daylight
/// blue as noon's.
///
/// The two terms are the two halves of a single-scattering sky, taken straight from the fields the
/// game feeds its own sky shader:
///   RAYLEIGH - the blue term. Wavelength-dependent scattering, so its hue is fixed; the palette's
///     <c>SkyRParam_rayleigh_amplifier</c> says how much of it there is (noon 1.0, night 0.25,
///     blood moon 0.0). Its normalised hue (0.144, 0.398, 1.0) lands within a hair of the sky
///     colour the game authors by hand for its indoor-capture env (0.131, 0.304, 1.0), which is a
///     good sign the normalisation is right.
///   MIE - the haze term, forward-scattered and therefore the SUN's colour, which the palette gives
///     as <c>SkySunColor</c>, amplified by <c>SkyRParam_mie_amplifier</c> (noon 12, night 0,
///     blood moon 256).
/// Blending them by their own amplifiers reproduces the expected behaviour at every sample point:
/// noon is blue plus a warm haze, night is dim blue with no haze, and a blood moon - all Mie, no
/// Rayleigh, with a deep red <c>SkySunColor</c> - comes out red.
/// </summary>
public static class AmbientLighting
{
    const float AmbientSkyScale = 1.80f;
    const float AmbientGroundScale = 1.20f;

    /// <summary>The Mie amplifier of the reference daylight palette, so a palette matching it contributes an equal share of haze and blue rather than an arbitrary one.</summary>
    const float ReferenceMieAmplifier = 12.0f;
    /// <summary>Likewise for Rayleigh: the reference daylight palette's own amplifier is the unit.</summary>
    const float ReferenceRayleighAmplifier = 1.0f;

    /// <param name="ambientScale">The viewer's own ambient slider. The palette's authored <see cref="EnvPalette.AmbientScale"/> multiplies it here rather than replacing it, so a palette that declares one shifts the ambient without taking the slider away (and a palette that doesn't declares 1.0, changing nothing).</param>
    /// <param name="postfx">The real <c>agl::pfx::Sky</c> baseline (<see cref="SkyPostFxLibrary.LoadFromRomfs"/>) - null falls back to <see cref="SkyPostFx.Default"/>, the exact values this method used to have hardcoded.</param>
    public static (Vector3 Sky, Vector3 Ground) ResolveHemisphereColors(EnvPalette palette, float ambientScale, SkyPostFx? postfx = null)
    {
        postfx ??= SkyPostFx.Default;
        float scale = ambientScale * palette.AmbientScale;

        // An explicit HemisphereLight always wins - that is the real authored value when there is
        // one, and it is what WildRenderingSharp's own StudioLight preset uses to get a neutral ambient.
        if (palette.HasHemiColors)
        {
            float hemiScale = palette.HemiIntensity * scale;
            return (palette.HemiSkyColor * hemiScale, palette.HemiGroundColor * hemiScale);
        }

        Vector3 hue = SkyHue(palette, postfx);
        return (hue * (AmbientSkyScale * scale), postfx.GroundColor * (AmbientGroundScale * scale));
    }

    /// <summary>
    /// The colour of the DIRECTIONAL light hitting actors, for this palette.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hue comes from <c>SkySunColor</c>, which is the palette's own authored sun. WildRenderingSharp used
    /// to light actors with <c>BgDifColor * BgDifIntensity</c> instead - but BgDif is the
    /// BACKGROUND diffuse, i.e. the sky's colour, and the two genuinely differ. On
    /// <c>BloodyMoon_DarknessDragon</c> the authored sun is (1, 0.140, 0.158), a deep red, while
    /// BgDifColor is (1, 0.297, 0.214): more than TWICE the green, which lights the actor orange
    /// under a blood moon. At noon the two nearly coincide ((1, .922, .850) against
    /// (1, .922, .749)), which is exactly why this went unnoticed - the bug only shows on the
    /// palettes with a strongly coloured sun.
    /// </para>
    /// <para>
    /// The MAGNITUDE deliberately stays with BgDif. Every exposure/brightness decision in this
    /// pipeline was calibrated against that number, and <c>SkySunColorIntensity</c> is on a
    /// different scale entirely (18 at noon against BgDifIntensity 9; 1 on the dragon palette
    /// against 2) - taking it raw would make noon twice as bright and the blood moon half as
    /// bright, re-grading every palette in the name of a colour fix. This is the same
    /// hue-from-one-field, brightness-from-another split the sky tint already uses for FogColor.
    /// </para>
    /// <para>
    /// <c>SkySunColorNoUse</c> is the palette's own "ignore my SkySunColor" switch, so it falls
    /// back to the previous behaviour's hue rather than to an arbitrary white.
    /// </para>
    /// </remarks>
    public static Vector3 SunColor(EnvPalette palette)
    {
        Vector3 hue = palette.SkySunColorNoUse ? Normalize(palette.BgDifColor) : Normalize(palette.SkySunColor);
        var bg = palette.BgDifColor;
        float magnitude = MathF.Max(bg.X, MathF.Max(bg.Y, bg.Z)) * palette.BgDifIntensity;
        return hue * magnitude;
    }

    /// <summary>
    /// The sky's own colour for this palette, normalised to a hue (max component 1) so the caller
    /// owns the magnitude. A blend of the Rayleigh and Mie terms weighted by the palette's own
    /// amplifiers - see the class remarks for why those two and why weighted rather than summed
    /// (summing would let a blood moon's 256x Mie amplifier blow the ambient out by 20x).
    /// </summary>
    public static Vector3 SkyHue(EnvPalette palette, SkyPostFx? postfx = null)
    {
        Vector3 rayleigh = Normalize((postfx ?? SkyPostFx.Default).RayleighScatteringCoeff);

        float wRayleigh = MathF.Max(0f, palette.SkyRayleighAmplifier) / ReferenceRayleighAmplifier;
        float wMie = MathF.Max(0f, palette.SkyMieAmplifier) / ReferenceMieAmplifier;

        // SkySunColorNoUse is the palette's own switch for "ignore my SkySunColor"; with no sun
        // colour there is no meaningful haze tint, so the sky is pure Rayleigh.
        Vector3 mie = palette.SkySunColorNoUse ? rayleigh : Normalize(palette.SkySunColor);
        if (palette.SkySunColorNoUse)
            wMie = 0f;

        float total = wRayleigh + wMie;
        if (total <= 1e-6f)
            return rayleigh; // a palette that scatters neither still needs a sky to bounce off

        return Normalize((rayleigh * wRayleigh + mie * wMie) / total);
    }

    /// <summary>Public form of <see cref="Normalize"/> - a colour reduced to its hue, for callers that own the magnitude themselves (the sun sprite, the horizon fog colour).</summary>
    public static Vector3 NormaliseHue(Vector3 c) => Normalize(c);

    /// <summary>Scales a colour so its largest component is 1, leaving pure black alone. Hue only - magnitude is the caller's business.</summary>
    static Vector3 Normalize(Vector3 c)
    {
        float max = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
        return max > 1e-6f ? c / max : c;
    }
}
