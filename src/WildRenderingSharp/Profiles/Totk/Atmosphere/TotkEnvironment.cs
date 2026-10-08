using System.Numerics;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Totk.Terrain;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>
/// TotK's environment for one frame: the palette in effect plus the static sky, cloud and colour-grade
/// settings that come with the game's data. Missing pieces fall back to the defaults.
/// </summary>
public sealed class TotkEnvironment(
    EnvPalette palette, SkyPostFx? skyPostFx = null, CloudPostFx? cloudPostFx = null,
    SkyBinLut? skyBin = null, ColorCorrectionPostFx? colorCorrection = null, ITerrainHost? terrain = null) : IFrameEnvironment
{
    /// <summary>A host whose terrain is shaded with the game's terrain programs, or null.</summary>
    public ITerrainHost? Terrain { get; } = terrain;

    public EnvPalette Palette { get; } = palette;
    public SkyPostFx SkyPostFx { get; } = skyPostFx ?? SkyPostFx.Default;
    public CloudPostFx CloudPostFx { get; } = cloudPostFx ?? CloudPostFx.Default;
    public SkyBinLut SkyBin { get; } = skyBin ?? SkyBinLut.Empty;
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
