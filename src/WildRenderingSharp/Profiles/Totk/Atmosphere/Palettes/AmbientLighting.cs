using System.Numerics;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;

/// <summary>Resolves a palette's hemisphere ambient (sky and ground) colours.</summary>
internal static class AmbientLighting
{
    const float AmbientSkyScale = 1.80f;
    const float AmbientGroundScale = 1.20f;

    // The Mie amplifier of the reference daylight palette, so a palette matching it contributes an equal share of haze and blue
    // rather than an arbitrary one.
    const float ReferenceMieAmplifier = 12.0f;
    const float ReferenceRayleighAmplifier = 1.0f;

    public static (Vector3 Sky, Vector3 Ground) ResolveHemisphereColors(EnvPalette palette, float ambientScale, SkyPostFx? postfx = null)
    {
        postfx ??= SkyPostFx.Default;
        float scale = ambientScale * palette.AmbientScale;

        // An explicit HemisphereLight always wins: it is the authored value, and how the StudioLight preset gets a neutral ambient.
        if (palette.HasHemiColors)
        {
            float hemiScale = palette.HemiIntensity * scale;
            return (palette.HemiSkyColor * hemiScale, palette.HemiGroundColor * hemiScale);
        }

        Vector3 hue = SkyHue(palette, postfx);
        return (hue * (AmbientSkyScale * scale), postfx.GroundColor * (AmbientGroundScale * scale));
    }

    // The directional light is the palette's background diffuse colour at its intensity; the sky's sun colour tints the scattering and is blue at sunrise.
    public static Vector3 SunColor(EnvPalette palette) => palette.BgDifColor * palette.BgDifIntensity;

    public static Vector3 SkyHue(EnvPalette palette, SkyPostFx? postfx = null)
    {
        Vector3 rayleigh = Normalize((postfx ?? SkyPostFx.Default).RayleighScatteringCoeff);

        float wRayleigh = MathF.Max(0f, palette.SkyRayleighAmplifier) / ReferenceRayleighAmplifier;
        float wMie = MathF.Max(0f, palette.SkyMieAmplifier) / ReferenceMieAmplifier;

        // With no sun colour there is no meaningful haze tint, so the sky is pure Rayleigh.
        Vector3 mie = palette.SkySunColorNoUse ? rayleigh : Normalize(palette.SkySunColor);
        if (palette.SkySunColorNoUse)
            wMie = 0f;

        float total = wRayleigh + wMie;
        if (total <= 1e-6f)
            return rayleigh; // a palette that scatters neither still needs a sky to bounce off

        return Normalize((rayleigh * wRayleigh + mie * wMie) / total);
    }

    // The share of the sun's light that sunlit ground throws back up into the shade, in the greenish white the ground gives it. The game's environment probe sees
    // that ground; this stands in for it as an addition to the ground colour the ambient light reads.
    // TODO: the share and the hue are fitted to one screenshot of a grassy field, not read from the game, whose probe sees the real ground under the camera.
    public static Vector3 GroundBounce(Vector3 sunColor, float sunElevation, float strength)
    {
        float lit = MathF.Max(0f, MathF.Sin(sunElevation));
        return new Vector3(0.61f, 1f, 0.83f) * (MathF.Max(sunColor.X, MathF.Max(sunColor.Y, sunColor.Z)) * lit * strength);
    }

    public static Vector3 NormaliseHue(Vector3 c) => Normalize(c);

    static Vector3 Normalize(Vector3 c)
    {
        float max = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
        return max > 1e-6f ? c / max : c;
    }
}
