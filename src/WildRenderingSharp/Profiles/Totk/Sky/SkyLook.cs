using System.Numerics;
using WildRenderingSharp.Profiles.Totk.Atmosphere;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>What a palette contributes to the sky bake: its Mie phase asymmetry, its two amplifiers and the colour of the sun being scattered.</summary>
public readonly record struct SkyLook(float MieAsymmetry, float RayleighAmplifier, float MieAmplifier, Vector3 SunRadiance)
{
    /// <summary>The noon palette's values, which are also what the bake's captured constants hold.</summary>
    public static readonly SkyLook Noon = new(0.75f, 1f, 12f, new Vector3(18f, 16.588242f, 15.3f));

    public static SkyLook From(EnvPalette? palette)
    {
        if (palette is null)
            return Noon;
        Vector3 sun = palette.SkySunColorNoUse ? Noon.SunRadiance : palette.SkySunColor * palette.SkySunColorIntensity;
        return new SkyLook(palette.SkyMieSymmetrical, palette.SkyRayleighAmplifier, palette.SkyMieAmplifier, sun);
    }
}
