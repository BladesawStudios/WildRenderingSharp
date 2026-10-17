using System.Numerics;
using WildRenderingSharp.Profiles.Totk.Atmosphere;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;
using WildRenderingSharp.Profiles.Totk.Sky.PostFx;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>
/// The two fogs the pre-shading passes apply to the lit scene, in the terms their programs read: the scatter fog, which blends toward the sky's baked
/// inscatter along the view, and the ad hoc fog, a coloured haze that thickens with distance and thins toward the zenith.
/// </summary>
/// <remarks>
/// The sky's own ad hoc fog reads the same four values, so both take them from <see cref="SkyPostFxPass.Resolve"/> and the fog's colour is the sky's haze colour.
/// </remarks>
internal readonly record struct PreShadingFog(
    float ScatterNear, float ScatterFar, float ScatterAttenuation, float ScatterHorizontal, float ScatterDensity,
    float GroundStart, float GroundEnd, float GroundAttenuation, SkyPostFxPass.AdhocFog Haze, float LeafFogEnd,
    Vector3 VolumeColour, float VolumeDensity, float VolumeStart, float VolumeEnd, float VolumeMask)
{
    public static PreShadingFog From(TotkEnvironment environment, float skyIntensity, float strength)
    {
        // Off is the same fog with nothing in it.
        if (!environment.Settings.UseFog)
            return default(PreShadingFog) with { ScatterFar = 1f, GroundEnd = 1f, VolumeEnd = 1f, LeafFogEnd = LeafRange(environment.SkyPostFx) };

        var palette = environment.Palette;
        var sky = environment.SkyPostFx;
        float density = palette.TryGetFloat("SfParam_density", out float authored) ? authored : sky.ScatterFogDensity;
        float near = palette.TryGetFloat("SfParam_near", out float authoredNear) ? authoredNear : sky.ScatterFogNear;
        float far = palette.TryGetFloat("SfParam_far", out float authoredFar) ? authoredFar : sky.ScatterFogFar;
        return new PreShadingFog(near, far, palette.ScatterFogAttenuation, palette.ScatterFogHorizontal, density,
            palette.FogStart, palette.FogEnd, palette.AdhocFogAttenGrd,
            SkyPostFxPass.Resolve(palette, sky, skyIntensity, strength, normaliseHue: false), LeafRange(sky),
            palette.YFogColor * skyIntensity, palette.YFogDensity, palette.YFogStart,
            palette.TryGetFloat("YFogEnd", out float yFogEnd) ? yFogEnd : DefaultYFogEnd, environment.Settings.VolumeFogMask);
    }

    // The end of the low fog's ramp when the palette authors none: the world fog object's own, in genvb's master_field.
    const float DefaultYFogEnd = 240f;

    // Where the leaf pass has wholly replaced a leaf with the sky behind it: past where one can be seen, and whether or not the fog is on.
    static float LeafRange(SkyPostFx sky) => MathF.Max(sky.ScatterFogFar, 1f);
}
