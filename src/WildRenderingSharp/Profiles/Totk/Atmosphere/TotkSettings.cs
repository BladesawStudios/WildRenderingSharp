using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>The live-tunable sky, cloud, lens-flare and correction settings of the TotK profile.</summary>
public sealed class TotkSettings
{
    public string PaletteName { get; set; } = EnvPaletteLibrary.StudioLightPaletteName;

    /// <summary>Applies <c>KnownMaterialFixes</c>, and the forward-program regexes of <c>KnownDecompilerCorrections</c> when a model loads.</summary>
    public bool EnableKnownMaterialFixes { get; set; } = true;

    public float AtmosphereIntensity { get; set; } = 1.0f;

    /// <summary>Draws the game's <c>agl_cloud</c> program instead of the FBM approximation in <c>BackgroundPass</c>.</summary>
    public bool UseRealCloudDome { get; set; } = true;

    /// <summary>Draws the game's <c>agl_sky_postfx_sky</c> over the baked table instead of <c>BackgroundPass</c>'s ray-march.</summary>
    public bool UseRealSkyShader { get; set; } = true;

    /// <summary>
    /// The <c>USE_ADHOC_FOG</c> term. Off by default: verified against a capture, but it comes out as a near-uniform haze rather
    /// than the horizon band it was written for.
    /// </summary>
    public bool UseSkyFog { get; set; }

    public float SkyFogStrength { get; set; } = 1f;

    /// <summary>Normalises the palette's fog colour to full brightness; authored magnitudes differ 17x between palettes.</summary>
    public bool SkyFogNormaliseHue { get; set; } = true;

    /// <summary>How much of the palette's <c>BgDifColor</c> bleeds into the sky shader's result; 0 is what the game does.</summary>
    public float SkyPaletteTint { get; set; } = 1.0f;

    /// <summary>
    /// Where the sky's brightest texel lands before the tonemap. Above 1 on purpose: the highlight compression and HDR compose that
    /// follow bring it back, and aiming at 1 gives a flat sky.
    /// </summary>
    public float SkyHdrLevel { get; set; } = 1.8f;

    public bool UseLensFlare { get; set; } = true;

    /// <summary>Brightness a pixel must exceed, after exposure, to throw ghosts.</summary>
    public float LensFlareThreshold { get; set; } = 24f;

    public bool LensFlareSkyOnly { get; set; } = true;
    public float LensFlareGhostSpacing { get; set; } = 0.32f;
    public float LensFlareHaloRadius { get; set; } = 0.28f;
    public float LensFlareIntensity { get; set; } = 0.35f;

    public bool ShowSun { get; set; } = true;
    public bool ShowMoon { get; set; } = true;

    /// <summary>Which of the eight authored moon-phase sprites to draw, 1-8.</summary>
    public int MoonPhase { get; set; } = 4;

    public float SunAngularRadiusDegrees { get; set; } = 2.0f;
    public float MoonAngularRadiusDegrees { get; set; } = 1.5f;
    public float SunSpriteIntensity { get; set; } = 1f;
    public float MoonSpriteIntensity { get; set; } = 1f;

    /// <summary>Moon direction in radians, independent of the sun's.</summary>
    public float MoonElevation { get; set; } = 0.6f;
    public float MoonAzimuth { get; set; } = 3.1416f;

    /// <summary>Multiplies the cloud colour; applied to the shader's final multiplier, not its input colours.</summary>
    public float CloudBrightness { get; set; } = 1.0f;

    public bool AnimateClouds { get; set; } = true;
    public CloudFadeSettings CloudFade { get; set; } = new();

    /// <summary>The cloud dome's resolution as a fraction of the viewport; its cost is fill rate.</summary>
    public float CloudResolutionScale { get; set; } = 0.5f;
}
