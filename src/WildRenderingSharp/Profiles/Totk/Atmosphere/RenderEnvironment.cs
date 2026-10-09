using WildRenderingSharp.Profiles.Totk.Atmosphere.Clouds;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;
using WildRenderingSharp.Profiles.Totk.Terrain;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>
/// The world data a frame is lit by that comes from the romfs rather than a model: the environment palettes, the sky and cloud
/// postfx baseline, the final colour grade, and the sky LUT.
/// </summary>
public sealed class RenderEnvironment
{
    public EnvPaletteLibrary Palettes { get; private set; } = EnvPaletteLibrary.Empty();

    public SkyPostFx SkyPostFx { get; private set; } = SkyPostFx.Default;

    public CloudPostFx CloudPostFx { get; private set; } = CloudPostFx.Default;

    public ColorCorrectionPostFx ColorCorrection { get; private set; } = ColorCorrectionPostFx.Default;

    public CloudWeather CloudWeather { get; private set; } = CloudWeather.Empty;

    public void LoadFrom(IRomAccess? rom)
    {
        Palettes = EnvPaletteLibrary.Load(rom);
        (SkyPostFx, CloudPostFx, ColorCorrection) = SkyPostFxLibrary.Load(rom);
        CloudWeather = CloudWeather.Load(rom);
    }

    public TotkEnvironment Resolve(TotkSettings settings, ITerrainHost? terrain = null) =>
        new(Palettes.Get(settings.PaletteName), SkyPostFx, CloudPostFx, ColorCorrection, terrain, settings, CloudWeather);


    public static RenderEnvironment Load(IRomAccess? rom)
    {
        var environment = new RenderEnvironment();
        environment.LoadFrom(rom);
        return environment;
    }
}
