using System.Numerics;

namespace WildRenderingSharp.Rendering;

/// <summary>What fills the pixels no placed actor covers; see <see cref="Pipeline.BackgroundPass"/> for the per-mode rendering.</summary>
public enum BackgroundMode
{
    /// <summary>A flat colour (<see cref="LightingContext.BackgroundColor"/>), the closest thing to a studio backdrop.</summary>
    Color,
    /// <summary>Alpha 0: nothing behind the model. Carried through to file export (see <see cref="Pipeline.PresentPass"/>'s <c>alphaSource</c>).</summary>
    Transparent,
    /// <summary>The ray-marched Rayleigh and Mie sky, parameterised by the current palette's <c>SkyRParam_*</c> and <c>SkySunColor</c> fields; see <see cref="Pipeline.BackgroundPass"/>.</summary>
    TotkSky,
}

/// <summary>The live-tunable lighting and exposure state of a viewing session.</summary>
public class LightingContext
{
    /// <summary>What fills the space around the placed actors; see <see cref="BackgroundMode"/>.</summary>
    public BackgroundMode Background { get; set; } = BackgroundMode.Color;

    /// <summary>The flat colour <see cref="BackgroundMode.Color"/> clears to: a near-black rather than pure black.</summary>
    public Vector3 BackgroundColor { get; set; } = new(0.000075f, 0.00008f, 0.0001f);

    /// <summary>Multiplier on the brightness of the sky background's scattering integral (see <see cref="Pipeline.BackgroundPass"/>).</summary>
    public float AtmosphereIntensity { get; set; } = 1.0f;

    /// <summary>
    /// Draw the game's <c>agl_cloud</c> program (<c>CloudDomePass</c>) instead of the FBM
    /// approximation in <see cref="Pipeline.BackgroundPass"/>. The program's uniform block is populated where a
    /// capture confirmed the offset; its masks are the game's own textures (not romfs assets, see <c>res/cloud/README.md</c>).
    /// </summary>
    public bool UseRealCloudDome { get; set; } = true;

    /// <summary>Draw the sky with the game's <c>agl_sky_postfx_sky</c> over the baked table, instead of <c>BackgroundPass</c>'s ray-march (kept for comparison).</summary>
    public bool UseRealSkyShader { get; set; } = true;

    /// <summary>
    /// The <c>USE_ADHOC_FOG</c> term. Off by default: decoded and verified against a capture, but it is a
    /// near-uniform haze rather than the horizon band it was implemented for (with the exponent at 0.5,
    /// <c>pow(up, 0.5)</c> is 0.22 only 3 degrees above the horizon). The band is more likely the Mie term in
    /// the table itself (a blood moon authors <c>SkyRParam_mie_amplifier = 256</c>).
    /// </summary>
    public bool UseSkyFog { get; set; }

    /// <summary>Multiplier on the fog band's density; 1 is the palette's authored value.</summary>
    public float SkyFogStrength { get; set; } = 1f;

    // Lens flare (LensFlarePass).
    public bool UseLensFlare { get; set; } = true;
    /// <summary>Brightness a pixel must exceed, after exposure (display units, 1 = white), to throw ghosts. High, so only a sun-like source does.</summary>
    public float LensFlareThreshold { get; set; } = 24f;
    /// <summary>Only sky (far-plane) pixels throw ghosts; the game's flare is a sun effect.</summary>
    public bool LensFlareSkyOnly { get; set; } = true;
    /// <summary>Ghost spacing: the step from a pixel toward screen centre.</summary>
    public float LensFlareGhostSpacing { get; set; } = 0.32f;
    public float LensFlareHaloRadius { get; set; } = 0.28f;
    public float LensFlareIntensity { get; set; } = 0.35f;

    // Sun and moon (SkyBodyPass), drawn from the game's sprites.
    public bool ShowSun { get; set; } = true;
    public bool ShowMoon { get; set; } = true;

    /// <summary>Which of the eight authored moon-phase sprites to draw (1-8).</summary>
    public int MoonPhase { get; set; } = 4;

    /// <summary>Sun sprite's angular radius in degrees. The real sun is about 0.27, but the sprite is a glow extending past the disc.</summary>
    public float SunAngularRadiusDegrees { get; set; } = 2.0f;
    public float MoonAngularRadiusDegrees { get; set; } = 1.5f;

    /// <summary>Brightness multiplier for the sun sprite, on top of the palette's own sun colour.</summary>
    public float SunSpriteIntensity { get; set; } = 1f;
    public float MoonSpriteIntensity { get; set; } = 1f;

    /// <summary>Moon direction, independent of the sun's; defaults to roughly opposite it.</summary>
    public float MoonElevation { get; set; } = 0.6f;
    public float MoonAzimuth { get; set; } = 3.1416f;

    /// <summary>Normalise the palette's FogColor to full brightness for the horizon fog. Raw matches the one captured frame, but authored magnitudes differ 17x between palettes.</summary>
    public bool SkyFogNormaliseHue { get; set; } = true;

    /// <summary>Live multiplier on the cloud colour; 1 is as authored by the palette's layer, or by <c>master_field.baglclwd</c> if it authors none.</summary>
    /// <remarks>Applied to the shader's final multiplier (<c>Common[25].y</c>), not the input colours, which <c>CloudColorScale</c> re-amplifies.</remarks>
    public float CloudBrightness { get; set; } = 1.0f;

    /// <summary>Advance the cloud dome's noise-scroll offsets over time. Off freezes them.</summary>
    public bool AnimateClouds { get; set; } = true;

    /// <summary>The renderer's own distance fade for the cloud dome; see <c>CloudDistanceFade</c>.</summary>
    public CloudFadeSettings CloudFade { get; set; } = new();

    /// <summary>How much of the palette's own colour bleeds into the sky shader's result, 0-1.</summary>
    /// <remarks>
    /// The game bakes an atmosphere table per palette; one is baked here, so without this a palette changes only
    /// the sky's brightness. It drives the shader's own blend bias (<c>RenderInfo[6].w</c>); 0 is what the game
    /// does. Defaults to 1, where the palette decides the hue outright: anything less lerps toward white, which for
    /// a fog hue with a zero channel (BloodyMoon's is (1, 0, 0.222)) is the difference between red and orange.
    /// The colour blended in is the palette's <c>BgDifColor</c>, the field that carries per-palette sky colour.
    /// </remarks>
    public float SkyPaletteTint { get; set; } = 1.0f;

    /// <summary>The value the sky's brightest texel is aimed at before the tonemap, for a neutral palette.</summary>
    /// <remarks>
    /// Above 1 on purpose: highlight compression and the game's HDR compose follow and bring it to display range,
    /// and aiming at or below 1 gives a flat sky. It is a control because it interacts with bloom: with a typical
    /// palette threshold of 0.15 and a peak of 1.8 nearly the whole sky blooms, so lowering this or the bloom
    /// intensity trades glow for saturation.
    /// </remarks>
    public float SkyHdrLevel { get; set; } = 1.8f;

    /// <summary>Resolution the cloud dome renders at, as a fraction of the viewport; 0.5 is a quarter of the fragments. The program is heavy and the dome covers most of the screen, so its cost is fill rate.</summary>
    public float CloudResolutionScale { get; set; } = 0.5f;

    /// <summary>Sun elevation, radians. With <see cref="SunAzimuth"/> it gives the sun direction.</summary>
    public float SunElevation { get; set; } = 0.6f;
    public float SunAzimuth { get; set; }

    /// <summary>
    /// Multiplies the HDR frame before the tonemap. 2.5 is chosen by hand; the game authors 1.0 against lighting
    /// this renderer lacks (local lights, probe IBL). See <see cref="Pipeline.ExposureMeter"/>.
    /// </summary>
    public float Exposure { get; set; } = 2.5f;
    public float AmbientScale { get; set; } = 1.0f;
    public float MidScale { get; set; } = 1.0f;
    public float HighlightScale { get; set; } = 1.0f;

    /// <summary>
    /// Correction for the lit path being about 5.5x too bright. It rides on the lit path alone, separate from
    /// <see cref="Exposure"/>, so emission is not attenuated along with the lighting.
    /// </summary>
    public float SceneGain { get; set; } = 1f / 5.5f;
    /// <summary>Multiplies authored emission; 1 is as authored. Emission is deliberately not scaled by <see cref="Exposure"/>.</summary>
    public float EmissionScale { get; set; } = 1.0f;

    /// <summary>Multiplies the palette's own authored <c>BloomIntensity</c>; 1.0 = exactly what the palette asks for, 0 = off.</summary>
    public float BloomIntensity { get; set; } = 1.0f;

    public string PaletteName { get; set; } = EnvPaletteLibrary.StudioLightPaletteName;
    public bool IconCaptureMode { get; set; }

    /// <summary>
    /// Manual, individually verified corrections for game behaviour the shader-driven pipeline cannot derive:
    /// cases where the correctly decompiled shader and correctly read material data still do not reproduce the
    /// game's result, and investigation found no mechanism that explains the gap. See
    /// <c>KnownMaterialFixes</c> for each fix. Also gates the forward-program regex of
    /// <c>KnownDecompilerCorrections</c> through <c>ModelLoader.Load</c>'s
    /// <c>enableKnownDecompilerCorrections</c>, to A/B whether the offline decompiler's DebugMode already fixes
    /// that bug. On by default; each fix is scoped narrowly by exact texture, material or program role.
    /// </summary>
    public bool EnableKnownMaterialFixes { get; set; } = true;

    /// <summary>
    /// Whether <c>cTex_DeferredLightPrePass</c> holds a synthetic sun-plus-ambient term (see <see cref="Pipeline.LightPrePass"/>).
    /// Off by default: in the game it accumulates local lights only, and the resolve normalises it to unit
    /// brightness where it is non-zero, so a synthetic fill gave every lit surface a second full-strength light.
    /// </summary>
    public bool SyntheticLightPrePass { get; set; }

    /// <summary>The ground reference grid, a viewport display toggle rather than a shading option.</summary>
    public bool ShowGrid { get; set; } = true;
}
