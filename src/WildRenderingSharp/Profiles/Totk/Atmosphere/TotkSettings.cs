using System.Numerics;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Clouds;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>The live-tunable sky, cloud, lens-flare and correction settings of the TotK profile.</summary>
public sealed class TotkSettings
{
    public string PaletteName { get; set; } = EnvPaletteLibrary.StudioLightPaletteName;

    public bool EnableKnownMaterialFixes { get; set; } = true;

    public float AtmosphereIntensity { get; set; } = 1.0f;

    public bool UseRealCloudDome { get; set; } = true;

    public bool UseRealSkyShader { get; set; } = true;

    public bool UseSkyFog { get; set; } = true;

    // The scatter and ad hoc fog the pre-shading passes apply to the lit scene.
    public bool UseFog { get; set; } = true;

    // How much of the screen the fog volume mask covers, 0 to 1. The game's pre-shading applies the palette's low fog (YFog) and volume tint only where its
    // volume mask says so, and what that mask holds out of doors is not known: 0 is neither, and 1 is the whole scene in both.
    public float VolumeFogMask { get; set; } = 1f;

    // Whether the volume mask is the game's own cylinder of fog drawn around the camera, in place of the one share above. It needs the effect prepared.
    public bool UseVolumeMaskEffect { get; set; } = true;

    // Whether the game's field haze, the pale warm veil over the distance, is laid over the image. It needs the effect prepared.
    public bool UseFieldHaze { get; set; } = true;

    // How much thicker the haze is than the sprites' own numbers make it, fitted to a screenshot of the game.
    public float FieldHazeStrength { get; set; } = 0.8f;

    // The power the haze's optical depth is raised to, below one so that near haze is thicker for a given far haze: 1 is the plain exponential of the sprites' own numbers.
    public float FieldHazeFalloff { get; set; } = 0.4f;

    // How much of the haze reaches the sky, 0 to 1: the game leaves the sky out, but a little of it softens the horizon.
    public float FieldHazeSkyInfluence { get; set; } = 0.05f;

    // How much of the sun's light the ground throws back up as ambient light, in units of the sun's own: 0 leaves the ambient ground colour as the palette gives it.
    public float GroundBounce { get; set; } = 0.015f;

    // The light the hall seen through a shrine's doorway is lit with, from above and below: the game's DungeonEntranceParam colours (1) or the frame's own ambient (0), and
    // how much of it. The game's own is all of its colours, once.
    public float EntranceAmbientAuthored { get; set; } = 1f;

    public float EntranceAmbientScale { get; set; } = 1f;

    // How far the sun shadow cascades push a caster back by the slope of its surface to the light, so a surface the sun meets at a graze does not shadow itself.
    public float ShadowSlopeOffset { get; set; }

    // Whether the placed actors' shapes are drawn from shared buffers, one multi-draw for each program, in place of one draw for each shape.
    public bool MergedDraws { get; set; }

    public float SkyFogStrength { get; set; } = 1f;

    public bool SkyFogNormaliseHue { get; set; } = true;

    public float SkyPaletteTint { get; set; }

    // How far the sky shader blends the haze colour (the palette's fog colour) into the table where the table is thick, which is toward the horizon.
    public float SkyHorizonHaze { get; set; }


    // The host's gloom, where it supplies one (see IGloomSource).
    public bool UseGloom { get; set; } = true;

    public bool UseLensFlare { get; set; } = true;

    public float LensFlareThreshold { get; set; } = 60f;

    public bool LensFlareSkyOnly { get; set; } = true;
    public float LensFlareGhostSpacing { get; set; } = 0.32f;
    public float LensFlareHaloRadius { get; set; } = 0.28f;
    public float LensFlareIntensity { get; set; } = 0.35f;

    public bool ShowSun { get; set; } = true;
    public bool ShowMoon { get; set; } = true;

    public int MoonPhase { get; set; } = 4;

    public float SunAngularRadiusDegrees { get; set; } = 2.0f;
    public float MoonAngularRadiusDegrees { get; set; } = 1.5f;
    public float SunSpriteIntensity { get; set; } = 1f;
    public float MoonSpriteIntensity { get; set; } = 1f;

    public float MoonElevation { get; set; } = 0.6f;
    public float MoonAzimuth { get; set; } = 3.1416f;

    public float CloudBrightness { get; set; } = 1.0f;

    public bool AnimateClouds { get; set; } = true;
    public CloudFadeSettings CloudFade { get; set; } = new();

    public float CloudResolutionScale { get; set; } = 0.5f;

    // Which of the game's cloud weathers (the PrequelCwCloud file number, 0 to 2) the layers look like.
    public int CloudWeatherSet { get; set; }

    // Which of the three cloud layers are drawn, still subject to the weather leaving one invisible.
    public bool[] CloudLayerEnabled { get; } = [true, true, true];

    // The unit wind the clouds scroll along; the default is the one a capture of the game's cloud draw implies.
    public Vector2 CloudWind { get; set; } = CloudLayerResolver.CapturedWind;
}
