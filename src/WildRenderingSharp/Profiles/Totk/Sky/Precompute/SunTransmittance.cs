using System.Numerics;
using WildRenderingSharp.Profiles.Totk.Atmosphere;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;

namespace WildRenderingSharp.Profiles.Totk.Sky.Precompute;

/// <summary>The colour of the sun's disc after its light has crossed the atmosphere, from the same coefficients the sky bake uses.</summary>
static class SunTransmittance
{
    // Mie extinction is its scattering over this albedo, as in Bruneton's model.
    const float MieSingleScatteringAlbedo = 0.9f;

    public static Vector3 Colour(SkyPostFx postfx, float rayleighAmplifier, float mieAmplifier, float elevationRadians)
    {
        float zenith = MathF.Max(0f, MathF.PI / 2f - elevationRadians);
        float mass = AirMass(zenith);

        Vector3 rayleigh = postfx.RayleighScatteringCoeff * rayleighAmplifier * postfx.RayleighBaseHeight;
        float mie = postfx.MieScatteringCoeff * mieAmplifier / MieSingleScatteringAlbedo * postfx.MieBaseHeight;
        Vector3 depth = (rayleigh + new Vector3(mie)) * mass;
        return new Vector3(MathF.Exp(-depth.X), MathF.Exp(-depth.Y), MathF.Exp(-depth.Z));
    }

    // Kasten and Young's relative air mass; finite at the horizon where 1/cos would not be.
    static float AirMass(float zenithRadians)
    {
        float degrees = float.RadiansToDegrees(zenithRadians);
        return 1f / (MathF.Cos(zenithRadians) + 0.50572f * MathF.Pow(MathF.Max(1e-3f, 96.07995f - degrees), -1.6364f));
    }
}
