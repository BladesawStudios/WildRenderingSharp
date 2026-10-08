using WildRenderingSharp.Profiles.Totk.Terrain;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>
/// The world data a frame is lit by that comes from the romfs rather than a model: the environment palettes,
/// the sky and cloud postfx baseline, the final colour grade, and the sky LUT.
/// </summary>
/// <remarks>
/// Loaded straight from the romfs at runtime (BYML/AAMP, no BFRES), except the sky LUT, which the
/// preparer extracts into the cache (<see cref="CacheLayout.SkyData"/>) because it needs Tegra
/// deswizzling. Without a romfs every member falls back to hand-transcribed defaults, so a frame
/// still renders.
/// </remarks>
public sealed class RenderEnvironment
{
    public EnvPaletteLibrary Palettes { get; private set; } = EnvPaletteLibrary.Empty();

    /// <summary>The real <c>agl::pfx::Sky</c> static baseline - see <see cref="SkyPostFxLibrary.LoadFromRomfs"/>.</summary>
    public SkyPostFx SkyPostFx { get; private set; } = SkyPostFx.Default;

    public CloudPostFx CloudPostFx { get; private set; } = CloudPostFx.Default;

    /// <summary>The game's own final grade (<c>postfx/master_field.baglccr</c>) - see <see cref="WildRenderingSharp.Profiles.Totk.PostProcess.ColorCorrectionPass"/>.</summary>
    public ColorCorrectionPostFx ColorCorrection { get; private set; } = ColorCorrectionPostFx.Default;

    /// <summary>The real sky-scattering LUT (<c>res/master_field.skybin</c>).</summary>
    public SkyBinLut SkyBin { get; private set; } = SkyBinLut.Empty;

    /// <summary>Reloads the palettes and postfx from <paramref name="romfsRoot"/> (null for defaults).</summary>
    public void LoadFromRomfs(string? romfsRoot)
    {
        Palettes = EnvPaletteLibrary.LoadFromRomfs(romfsRoot);
        (SkyPostFx, CloudPostFx, ColorCorrection) = SkyPostFxLibrary.LoadFromRomfs(romfsRoot);
    }

    /// <summary>The environment for the palette <paramref name="settings"/> names, with the game's static sky, cloud and grade settings.</summary>
    public TotkEnvironment Resolve(TotkSettings settings, ITerrainHost? terrain = null) =>
        new(Palettes.Get(settings.PaletteName), SkyPostFx, CloudPostFx, SkyBin, ColorCorrection, terrain, settings);

    /// <summary>Reloads the sky LUT from the cache, where the preparer put it.</summary>
    public void LoadSkyBin(CacheLayout cache) => SkyBin = SkyBinLut.LoadFromCache(cache.SkyData);

    /// <summary>A fully loaded environment.</summary>
    public static RenderEnvironment Load(string? romfsRoot, CacheLayout cache)
    {
        var environment = new RenderEnvironment();
        environment.LoadFromRomfs(romfsRoot);
        environment.LoadSkyBin(cache);
        return environment;
    }
}
