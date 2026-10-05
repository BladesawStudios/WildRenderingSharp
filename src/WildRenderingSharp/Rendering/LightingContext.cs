using System.Numerics;

namespace WildRenderingSharp.Rendering;

/// <summary>
/// What fills the pixels no placed actor covers - see <see cref="Pipeline.BackgroundPass"/> for the
/// actual per-mode rendering.
/// </summary>
public enum BackgroundMode
{
    /// <summary>A flat, user-chosen colour (<see cref="LightingContext.BackgroundColor"/>) - the closest equivalent to a plain studio backdrop.</summary>
    Color,
    /// <summary>Alpha 0 - nothing behind the model at all. Threaded all the way through to file export (PNG/HDR), not just the live preview - see <see cref="Pipeline.PresentPass"/>'s <c>alphaSource</c> parameter.</summary>
    Transparent,
    /// <summary>A real Rayleigh+Mie single-scattering sky, parameterised by the CURRENTLY SELECTED palette's own authored <c>SkyRParam_*</c>/<c>SkySunColor</c> fields (the same palette <see cref="PaletteName"/> already drives the rest of the lighting from) - see <see cref="Pipeline.BackgroundPass"/>.</summary>
    TotkSky,
}

/// <summary>
/// The live-tunable lighting/exposure state a viewer session carries - mirrors the parameters
/// <c>viewer.Viewer</c> exposes as keyboard-adjustable state (exposure, ambient/mid/highlight
/// scale, bloom, palette, icon-capture preset).
/// </summary>
public class LightingContext
{
    /// <summary>What fills the space behind/around the placed actor(s) - see <see cref="BackgroundMode"/>.</summary>
    public BackgroundMode Background { get; set; } = BackgroundMode.Color;

    /// <summary>
    /// The flat colour <see cref="BackgroundMode.Color"/> clears to - defaults to the same
    /// near-black the pipeline always used before this was configurable (see
    /// <c>Pipeline.BackgroundPass</c>'s own remarks on why that exact tuple, not pure black), so an
    /// existing session's look doesn't change until this is touched.
    /// </summary>
    public Vector3 BackgroundColor { get; set; } = new(0.000075f, 0.00008f, 0.0001f);

    /// <summary>
    /// User-facing multiplier on the TotK Sky background's real single-scattering atmosphere
    /// integral (<see cref="Pipeline.BackgroundPass"/> - a real ray-marched Rayleigh+Mie
    /// optical-depth integral, driven by the real physical parameters extracted from
    /// <c>master_field.baglsky</c>, the same technique the real game's own <c>agl::pfx::Sky</c>
    /// bakes via genuine Bruneton &amp; Neyret precomputed-scattering textures - see that pass's
    /// own remarks). Exists because repeated blind guesses at a fixed calibration constant in code
    /// kept coming back "still very bright and washed out" with no way for me to see the actual
    /// render - this is a live slider so the user can tune the overall brightness directly in the
    /// running app instead of another edit/rebuild/screenshot round trip.
    /// </summary>
    public float AtmosphereIntensity { get; set; } = 1.0f;

    /// <summary>
    /// Draw the real, decompiled <c>agl_cloud</c> shader (<see cref="Pipeline.CloudDomePass"/>)
    /// instead of the hand-tuned FBM approximation in <see cref="Pipeline.BackgroundPass"/>.
    /// On by default: selecting TotK Sky means the game's real shaders, with no separate opt-in.
    ///
    /// Its "Common" block is populated where a real capture confirmed the byte offset and left at
    /// captured baseline elsewhere, and the base/noise masks are the game's own textures (which
    /// are NOT romfs assets - see <c>res/cloud/README.md</c>).
    /// </summary>
    public bool UseRealCloudDome { get; set; } = true;

    /// <summary>
    /// Draw the sky with the game's own <c>agl_sky_postfx_sky</c> sampling the real Bruneton LUT,
    /// instead of <c>BackgroundPass</c>'s hand-written Rayleigh+Mie raymarch.
    /// </summary>
    /// <remarks>
    /// On by default: selecting TotK Sky now means the game's real shaders, with no separate
    /// opt-in. Still a property rather than a constant so a caller (or a future debug view) can
    /// fall back to <c>BackgroundPass</c>'s hand-written raymarch for comparison.
    /// </remarks>
    public bool UseRealSkyShader { get; set; } = true;

    /// <summary>
    /// The real <c>USE_ADHOC_FOG</c> term. OFF by default: it is decoded and capture-verified, but
    /// it does not produce the horizon BAND it was implemented for.
    /// </summary>
    /// <remarks>
    /// Reported in use as "just general fog, nothing on the horizon", and pink on a blood moon.
    /// That is consistent with the decode rather than against it: with the exponent at the captured
    /// 0.5, <c>pow(up, 0.5)</c> is already 0.22 only 3 degrees above the horizon, so the mix reaches
    /// its zenith value almost immediately and the whole sky gets a near-uniform wash. It is an
    /// atmospheric haze term, not the band. See CLAUDE.md - the band is far more likely the Mie
    /// term in the LUT itself (a blood moon authors <c>SkyRParam_mie_amplifier = 256</c>).
    /// </remarks>
    public bool UseSkyFog { get; set; }

    /// <summary>Multiplier on the horizon fog band's density - 1 is the palette's authored value.</summary>
    public float SkyFogStrength { get; set; } = 1f;

    // ---- lens flare: the game's own flare_filter_flare (LensFlarePass) ----
    public bool UseLensFlare { get; set; } = true;
    /// <summary>
    /// Brightness a pixel must exceed, AFTER exposure (display units, 1 = white), to throw ghosts.
    /// High on purpose - only a sun-like source should. ~24 matches the old pre-exposure 2.5 at
    /// exposure 9.5.
    /// </summary>
    public float LensFlareThreshold { get; set; } = 24f;
    /// <summary>Only sky (far-plane) pixels throw ghosts - the game's flare is a sun effect, so geometry never should.</summary>
    public bool LensFlareSkyOnly { get; set; } = true;
    /// <summary>Ghost spacing: the step from a pixel toward screen centre, so larger spreads the ghosts further apart.</summary>
    public float LensFlareGhostSpacing { get; set; } = 0.32f;
    public float LensFlareHaloRadius { get; set; } = 0.28f;
    public float LensFlareIntensity { get; set; } = 0.35f;

    // ---- sun and moon (SkyBodyPass), drawn from the game's own sprites ----
    public bool ShowSun { get; set; } = true;
    public bool ShowMoon { get; set; } = true;

    /// <summary>Which of the eight authored moon-phase sprites to draw (1-8).</summary>
    public int MoonPhase { get; set; } = 4;

    /// <summary>Sun sprite's angular RADIUS in degrees. The real sun is ~0.27, but the sprite is an authored glow that extends well past the disc, so the default is larger on purpose.</summary>
    public float SunAngularRadiusDegrees { get; set; } = 2.0f;
    public float MoonAngularRadiusDegrees { get; set; } = 1.5f;

    /// <summary>Brightness multiplier for the sun sprite, on top of the palette's own sun colour.</summary>
    public float SunSpriteIntensity { get; set; } = 1f;
    public float MoonSpriteIntensity { get; set; } = 1f;

    /// <summary>Moon direction, independent of the sun's. Defaults to roughly opposite the sun the way a full moon sits.</summary>
    public float MoonElevation { get; set; } = 0.6f;
    public float MoonAzimuth { get; set; } = 3.1416f;

    /// <summary>Normalise the palette's FogColor to full brightness before using it as the horizon fog colour. See <c>SkyPostFxPass.Resolve</c>: raw is what a real capture shows, but authored magnitudes differ 17x between palettes, so raw leaves a blood moon's band nearly black.</summary>
    public bool SkyFogNormaliseHue { get; set; } = true;

    /// <summary>
    /// Live multiplier on the cloud colour. 1.0 = as authored by the palette's Cloud0/Cloud1
    /// layer, or by <c>master_field.baglclwd</c> for a palette authoring no cloud layer.
    /// </summary>
    /// <remarks>
    /// Applied to the shader's FINAL output multiplier (<c>Common[25].y</c>), not to the input
    /// colour slots. Scaling those was arithmetically correct but useless: <c>CloudColorScale</c>
    /// re-amplified them afterwards, so every value still clipped to white and the control looked
    /// dead - see the remarks at its use in <c>CloudDomePass.BuildCommonBlock</c>.
    /// </remarks>
    public float CloudBrightness { get; set; } = 1.0f;

    /// <summary>
    /// Advance the cloud dome's two noise-scroll offsets over time, so the clouds drift and evolve
    /// the way they do in game. Off freezes them at their current offsets.
    /// </summary>
    public bool AnimateClouds { get; set; } = true;

    /// <summary>
    /// WildRenderingSharp's own distance fade for the cloud dome - see <see cref="Pipeline.CloudDistanceFade"/>
    /// for why this exists alongside the game's own, which has never produced a visible falloff.
    /// </summary>
    public CloudFadeSettings CloudFade { get; set; } = new();

    /// <summary>
    /// How much of the palette's own colour bleeds into the real sky shader's result, 0-1.
    /// </summary>
    /// <remarks>
    /// The game bakes a separate atmosphere LUT per palette; WildRenderingSharp bakes one from the AAMP, so
    /// without this a palette can only change the sky's brightness, never its colour. This drives
    /// the shader's OWN blend bias (<c>RenderInfo[6].w</c>) rather than inventing a term: 0 is
    /// exactly what the capture shows the game doing.
    ///
    /// Defaults to 1.0 - the palette decides the sky's hue outright. Anything less LERPS BACK
    /// TOWARD WHITE, and for a palette whose fog hue has a channel at exactly zero that is the
    /// whole difference between the authored colour and a washed-out one: BloodyMoon's fog hue is
    /// (1, 0, 0.222), so 0.75 puts 25% green back and the sky reads orange instead of red. The
    /// multiply preserves the LUT's spatial variation either way, so full strength constrains the
    /// hue without flattening the gradient.
    ///
    /// The colour blended in is the palette's <c>BgDifColor</c> - confirmed from the real palette
    /// data as the field that actually carries per-palette sky colour. Prequel_BloodyMoon_Night has
    /// BgDifColor (1.0, 0.297, 0.214), i.e. red, while its scattering amplifiers only make the
    /// atmosphere hazier (rayleigh 0.25, mie 24) and could never produce that colour.
    /// </remarks>
    public float SkyPaletteTint { get; set; } = 1.0f;

    /// <summary>
    /// Value the sky's brightest texel is aimed at BEFORE the tonemap, for a neutral palette.
    /// </summary>
    /// <remarks>
    /// Above 1 on purpose: highlight compression and the real <c>agl_hdr_compose</c> come after,
    /// and those are what turn HDR into display range - aiming at or below 1 gives a technically
    /// unclipped but visibly flat sky.
    ///
    /// It is a live control rather than a constant because it interacts with BLOOM, which is the
    /// other thing that washes a saturated sky out: bloom thresholds on the palette's own
    /// <c>BloomThreshold</c> (0.15 on most), so at a peak of 1.8 nearly the whole sky is above
    /// threshold and becomes a broad additive glow. Lowering this, or Bloom Intensity, is what
    /// trades glow for saturation.
    /// </remarks>
    public float SkyHdrLevel { get; set; } = 1.8f;

    /// <summary>
    /// Resolution the cloud dome is rendered at, as a fraction of the viewport. 0.5 = quarter the
    /// fragments.
    /// </summary>
    /// <remarks>
    /// The real agl_cloud fragment program is very heavy (unrolled procedural noise) and the dome
    /// covers nearly the whole screen at its true size, so its cost is pure fill rate and scales
    /// with viewport area. Clouds are soft enough that the upscale is not visible.
    /// </remarks>
    public float CloudResolutionScale { get; set; } = 0.5f;

    /// <summary>Sun elevation, radians. Together with <see cref="SunAzimuth"/> gives the sun direction (see <c>Viewer._sun_world</c>).</summary>
    public float SunElevation { get; set; } = 0.6f;
    public float SunAzimuth { get; set; }

    public float Exposure { get; set; } = 9.5f;
    public float AmbientScale { get; set; } = 1.0f;
    public float MidScale { get; set; } = 1.0f;
    public float HighlightScale { get; set; } = 1.0f;

    /// <summary>
    /// The lit path's ~5.5x-too-bright correction (see <c>MASK_COMPOSE_SRC</c>'s remarks) - rides
    /// on the lit path alone, separate from <see cref="Exposure"/>, so it stops attenuating
    /// emission along with the lighting.
    ///
    /// Defaults to the documented correction (1/5.5) rather than 1.0/neutral: this class's own
    /// comment already described the lit path as "~5.5x-too-bright," but the correction was never
    /// actually applied by default, unlike the emission side's equivalent fix
    /// (<c>DeferredResolvePass.Run</c>'s exposure divide-back), which IS always-on. Found while
    /// investigating why `Iris_Model__Mt_Eye` renders its pupil bright green: the raw iris mask
    /// texture (real, confirmed-correct content - genuinely green/black/red) feeds a real,
    /// correctly-computed lit-path contribution in `chara_grossy`'s own shading, and with no lit-
    /// path correction applied, that green diffuse term was competing on equal footing with (and
    /// visually dominating) the material's real, correctly-boosted warm emission instead of
    /// properly playing second fiddle to it.
    /// </summary>
    public float SceneGain { get; set; } = 1f / 5.5f;
    /// <summary>
    /// Multiplies the emission a material authored. 1.0 = as authored: emission is deliberately
    /// NOT scaled by <see cref="Exposure"/> (see <c>DeferredResolvePass.Run</c>), so this stays
    /// meaningful while the exposure calibration moves.
    /// </summary>
    public float EmissionScale { get; set; } = 1.0f;

    /// <summary>Multiplies the palette's own authored <c>BloomIntensity</c>; 1.0 = exactly what the palette asks for, 0 = off.</summary>
    public float BloomIntensity { get; set; } = 1.0f;

    public string PaletteName { get; set; } = EnvPaletteLibrary.StudioLightPaletteName;
    public bool IconCaptureMode { get; set; }

    /// <summary>
    /// "WildRenderingSharp Related Improvements": manual, individually-verified corrections for real game
    /// behavior WildRenderingSharp's shader-driven pipeline cannot derive automatically - see
    /// <see cref="Pipeline.KnownMaterialFixes"/> for the actual fixes and why each one exists.
    ///
    /// Every other visual difference this project chases is either a bug in how WildRenderingSharp feeds a
    /// REAL decompiled shader its inputs, or a genuine gap in WildRenderingSharp's own reimplementation
    /// (missing IBL, no preshading passes, and so on) - both are things static analysis of the
    /// shader corpus and material data can, in principle, find and fix. This category is
    /// different: it exists for cases where the real, correctly-decompiled shader and the real,
    /// correctly-read material data still don't reproduce the real game's on-screen result, and
    /// exhaustive investigation (shader logic, texture content, material data, shader-variant
    /// resolution, and - via Ghidra - the game's own option-to-behavior mapping) found no
    /// discoverable mechanism that explains the gap. The shipped game evidently has SOME real
    /// special-case handling here that isn't present anywhere in the decompiled corpus or the
    /// executable's own option-resolution code WildRenderingSharp can inspect - confirmed, not assumed, for
    /// the first entry (the DungeonBoss eye's iris texture) by directly observing the real game.
    ///
    /// Also gates <see cref="Pipeline.KnownDecompilerCorrections"/>'s forward-program regex (a
    /// different, older category - a confirmed DECOMPILER bug in otherwise-correctly-read real
    /// shader code, not a real-game-behavior gap) via <c>ModelLoader.Load</c>'s
    /// <c>enableKnownDecompilerCorrections</c> parameter, folded under the same switch now that the
    /// offline decompiler's DebugMode translation is suspected to fix that bug at its actual root -
    /// letting this toggle A/B "did DebugMode alone fix it" on a model reload, without a rebuild.
    ///
    /// Defaults to on: these are hand-verified against real observed behavior, not guesses, and
    /// each is scoped narrowly (by exact texture/material name, or by program role) so it can only
    /// ever affect the specific known-broken case it targets. Exposed as a toggle anyway so a fix
    /// can be compared against the raw, unmodified shader-driven result, and so a fix that turns
    /// out to be wrong - or unnecessary - can be turned off without a rebuild.
    /// </summary>
    public bool EnableKnownMaterialFixes { get; set; } = true;

    /// <summary>Blender-style ground reference grid (see <c>GridPass</c>) - a viewport display toggle, not a shading option, but lives here alongside the other per-frame display state <see cref="DeferredPipeline.RenderFrame"/> already reads from a <see cref="LightingContext"/>.</summary>
    public bool ShowGrid { get; set; } = true;
}
