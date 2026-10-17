using System.Numerics;
using WildRenderingSharp.Graphics.Contracts;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Clouds;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;
using WildRenderingSharp.Profiles.Totk.Terrain;
using WildRenderingSharp.Rendering.Lighting;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>
/// TotK's environment for one frame: the palette in effect plus the static sky, cloud and colour-grade settings that come with the
/// game's data.
/// </summary>
public sealed class TotkEnvironment(
    EnvPalette palette, SkyPostFx? skyPostFx = null, CloudPostFx? cloudPostFx = null,
    ColorCorrectionPostFx? colorCorrection = null, ITerrainHost? terrain = null,
    TotkSettings? settings = null, CloudWeather? cloudWeather = null, HdrComposeParam? hdrCompose = null) : IFrameEnvironment
{
    public ITerrainHost? Terrain { get; } = terrain;

    public TotkSettings Settings { get; } = settings ?? new();
    public EnvPalette Palette { get; } = palette;
    public SkyPostFx SkyPostFx { get; } = skyPostFx ?? SkyPostFx.Default;
    public CloudPostFx CloudPostFx { get; } = cloudPostFx ?? CloudPostFx.Default;
    public CloudWeather CloudWeather { get; } = cloudWeather ?? CloudWeather.Empty;
    public HdrComposeParam HdrCompose { get; } = hdrCompose ?? HdrComposeParam.Default;
    public ColorCorrectionPostFx ColorCorrection { get; } = colorCorrection ?? ColorCorrectionPostFx.Default;

    public EnvironmentLighting ResolveLighting(LightingContext lighting)
    {
        var (hemiSky, hemiGround) = AmbientLighting.ResolveHemisphereColors(Palette, lighting.AmbientScale, SkyPostFx);

        // The palette's own "ignore my tint" switch.
        Vector3 volumeMaskColor = Palette.VolumeMaskColorNoUse ? Vector3.Zero : Palette.VolumeMaskColor;
        float volumeMaskIntensity = Palette.VolumeMaskColorNoUse ? 0f : Palette.VolumeMaskIntensity;

        Vector3 sun = AmbientLighting.SunColor(Palette);
        hemiGround += AmbientLighting.GroundBounce(sun, lighting.SunElevation, Settings.GroundBounce);
        return new EnvironmentLighting(sun, hemiSky, hemiGround, volumeMaskColor, volumeMaskIntensity);
    }

    public bool HasOwnGeometry => Terrain is not null;

    public PresentGrade PresentGrade => Palette.ColorCorrectEnable
        ? new PresentGrade(Palette.ColorCorrectSaturation, Palette.ColorCorrectBrightness, Palette.ColorCorrectGamma)
        : PresentGrade.Neutral;
}
