using WildRenderingSharp.Profiles.Totk.Terrain;

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


    public void LoadFromRomfs(string? romfsRoot)
    {
        Palettes = EnvPaletteLibrary.LoadFromRomfs(romfsRoot);
        (SkyPostFx, CloudPostFx, ColorCorrection) = SkyPostFxLibrary.LoadFromRomfs(romfsRoot);
    }

    public TotkEnvironment Resolve(TotkSettings settings, ITerrainHost? terrain = null) =>
        new(Palettes.Get(settings.PaletteName), SkyPostFx, CloudPostFx, ColorCorrection, terrain, settings);


    public static RenderEnvironment Load(string? romfsRoot, CacheLayout cache)
    {
        var environment = new RenderEnvironment();
        environment.LoadFromRomfs(romfsRoot);
        return environment;
    }
}
