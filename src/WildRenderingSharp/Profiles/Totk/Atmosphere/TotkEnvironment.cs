using System.Numerics;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Totk.Terrain;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>
/// TotK's environment for one frame: the palette in effect plus the static sky, cloud and colour-grade settings that come with the
/// game's data.
/// </summary>
public sealed class TotkEnvironment(
    EnvPalette palette, SkyPostFx? skyPostFx = null, CloudPostFx? cloudPostFx = null,
    ColorCorrectionPostFx? colorCorrection = null, ITerrainHost? terrain = null,
    TotkSettings? settings = null) : IFrameEnvironment
{
    public ITerrainHost? Terrain { get; } = terrain;

    public TotkSettings Settings { get; } = settings ?? new();
    public EnvPalette Palette { get; } = palette;
    public SkyPostFx SkyPostFx { get; } = skyPostFx ?? SkyPostFx.Default;
    public CloudPostFx CloudPostFx { get; } = cloudPostFx ?? CloudPostFx.Default;
    public ColorCorrectionPostFx ColorCorrection { get; } = colorCorrection ?? ColorCorrectionPostFx.Default;

    public EnvironmentLighting ResolveLighting(LightingContext lighting)
    {
        var (hemiSky, hemiGround) = AmbientLighting.ResolveHemisphereColors(Palette, lighting.AmbientScale, SkyPostFx);

        // The palette's own "ignore my tint" switch.
        Vector3 volumeMaskColor = Palette.VolumeMaskColorNoUse ? Vector3.Zero : Palette.VolumeMaskColor;
        float volumeMaskIntensity = Palette.VolumeMaskColorNoUse ? 0f : Palette.VolumeMaskIntensity;

        return new EnvironmentLighting(AmbientLighting.SunColor(Palette), hemiSky, hemiGround, volumeMaskColor, volumeMaskIntensity);
    }

    public PresentGrade PresentGrade => Palette.ColorCorrectEnable
        ? new PresentGrade(Palette.ColorCorrectSaturation, Palette.ColorCorrectBrightness, Palette.ColorCorrectGamma)
        : PresentGrade.Neutral;
}
