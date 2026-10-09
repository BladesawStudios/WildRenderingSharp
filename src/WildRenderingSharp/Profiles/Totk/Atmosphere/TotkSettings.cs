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

    public bool UseSkyFog { get; set; }

    public float SkyFogStrength { get; set; } = 1f;

    public bool SkyFogNormaliseHue { get; set; } = true;

    public float SkyPaletteTint { get; set; }

    // How far the sky shader blends the haze colour (the palette's fog colour) into the table where the table is thick, which is toward the horizon.
    public float SkyHorizonHaze { get; set; }


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
