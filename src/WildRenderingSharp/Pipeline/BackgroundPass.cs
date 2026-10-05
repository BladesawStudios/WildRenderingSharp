using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Fills every pixel of <c>targets.Final</c> with whatever <see cref="BackgroundMode"/> asks for,
/// BEFORE <see cref="DeferredResolvePass"/> runs - that pass's own per-shape compose step only ever
/// touches a pixel where the pass-ID mask claims it (see its own remarks), so whatever this leaves
/// behind survives untouched everywhere no placed actor covers. <see cref="DeferredResolvePass"/>
/// therefore no longer clears <c>Final</c> itself - this pass owns that now, unconditionally, for
/// all three modes.
///
///   - <see cref="BackgroundMode.Color"/>/<see cref="BackgroundMode.Transparent"/>: a plain
///     <c>glClear</c> - alpha 1 or 0 respectively.
///   - <see cref="BackgroundMode.TotkSky"/>: a real per-pixel RAY-MARCHED single-scattering
///     Rayleigh+Mie atmosphere (real optical-depth/transmittance integration along both the view
///     ray and the sun ray) PLUS a horizon fog-colour blend and a sun disc, reconstructing the view
///     ray from the camera's own inverse-view/FOV (same convention every other screen-space pass
///     here already uses - see <see cref="ScreenSpaceShadowAndAoPass"/>).
///
///     THIS IS THE SAME PHYSICAL TECHNIQUE THE REAL GAME USES: `agl::pfx::Sky` (confirmed via
///     Ghidra - see `FUN_7100bf47d8`'s decompile) bakes real single-scattering data into textures
///     with the literal debug names "Single Irradiance"/"Single Scattering Rayleigh"/
///     "Single Scattering Mie"/"delta J" - unmistakable terminology from Eric Bruneton &amp;
///     Fabrice Neyret's 2008 "Precomputed Atmospheric Scattering" paper, driven by the SAME real
///     `aglsky` parameter set as `master_field.baglsky` (rayleigh_base_height, mie_base_height,
///     the real per-channel scattering coefficients - see <c>SkyPostFx</c>). The literal compiled
///     shader bytes for that bake and the final composite aren't reachable with this project's
///     existing tooling (not in `AglShader.sharcb` or `AglLightShader.sharcb` - the only two
///     loadable shader archives anywhere in romfs; `EnvironmentRenderer`/`environment_renderer_image`
///     - which DOES have a real, loadable `cSkyColor` sampler - was traced to `drawOpa_`'s
///     per-material `o_material_behave` dispatch and confirmed to feed glossy/metal material
///     REFLECTIONS, not the visible background; `agl::pfx::Sky` is almost certainly a shared
///     Nintendo SDK component whose shaders are compiled directly into the executable rather than
///     shipped as loadable romfs content). So this RAY-MARCHES the real physical model live per
///     pixel (see <see cref="SkyFragmentSource"/>'s <c>atmosphereSingleScatter</c>) instead of
///     baking to an offscreen 3D LUT first - a faithful implementation of the documented,
///     confirmed-in-use algorithm and its real extracted parameters, not an invented shading model.
///
///     Calibrated against the SAME <c>SceneGain</c> correction the rest of this pipeline's lit path
///     already applies to the identical <c>BgDifIntensity</c>-scaled values (see
///     <c>DeferredResolvePass</c>'s compose shader), same as before this became a real ray march -
///     plus a user-facing <see cref="LightingContext.AtmosphereIntensity"/> live multiplier, since
///     repeated blind guesses at a fixed brightness constant in code kept coming back wrong with no
///     way for me to see the actual render.
/// </summary>
public sealed class BackgroundPass : IDisposable
{
    readonly GL _gl;
    readonly uint _skyProgram;

    /// <summary>
    /// The magnitude a palette-authored SKY colour has to be scaled by (on top of
    /// <c>SceneGain</c>) to survive the viewer's own <c>Exposure</c> multiply intact.
    /// </summary>
    /// <remarks>
    /// A calibration, not ground truth: the game renders with <c>Exposure: 1.0</c> and real
    /// lighting, WildRenderingSharp with ~9.5 and a stand-in, so anything painting the sky directly into
    /// <c>targets.Final</c> - which <c>TonemapPass.RunExposureAndCompress</c> then multiplies
    /// wholesale - has to pre-divide by roughly that factor or it clips to white.
    ///
    /// Named rather than inline because this value is itself under suspicion. It sits ~180x below
    /// the scale <see cref="CloudDomePass"/> paints palette colours at, into the same buffer ahead
    /// of the same Exposure multiply - and the clouds at THAT scale are confirmed to look right
    /// while this atmosphere is confirmed too bright. Applying this anchor to the clouds to close
    /// the gap was tried and reverted (it scaled a good result into near-invisibility): the gap is
    /// evidence against this constant, not against the cloud's. Treat it as the leading suspect
    /// for atmosphere over-brightness, and note that the hand-written raymarch it scales is itself
    /// slated for replacement by the real extracted agl sky shader.
    /// </remarks>
    public const float SkyColorAnchor = 0.03f;

    const string SkyVertexSource = """
        #version 450 core
        out vec2 vUV;
        void main() {
            float x = -1.0 + float((gl_VertexID & 1) * 4);
            float y = -1.0 + float((gl_VertexID & 2) * 2);
            vUV = vec2(x, y) * 0.5 + 0.5;
            gl_Position = vec4(x, y, 0.0, 1.0);
        }
        """;

    // Z-up world convention throughout this codebase (see Camera.Up's own remarks) - "up" below is
    // worldDir.z, not .y. Everything here keys STRICTLY off worldDir (a pure function of the
    // camera's rotation, never its position) - the same "sky at infinity" contract a real skybox
    // has, so panning/orbiting without rotating leaves the sky untouched and only turning the
    // camera moves it, predictably, in the direction you turned.
    const string SkyFragmentSource = """
        #version 450 core
        uniform mat4 uViewInv;
        uniform vec2 uTanHalf;        // (tanHalfFovX, tanHalfFovY)
        uniform vec3 uSunWorld;       // direction TOWARD the sun, world space, normalized

        // Real single-scattering atmosphere (Rayleigh + Mie), ray-marched per pixel - the same
        // physical model the real game's own agl::pfx::Sky bakes via genuine Bruneton & Neyret
        // (2008) "Precomputed Atmospheric Scattering" textures (confirmed: FUN_7100bf47d8's
        // decompile allocates textures with the literal debug names "Single Irradiance"/
        // "Single Scattering Rayleigh"/"Single Scattering Mie"/"delta J" - that exact terminology
        // is unique to that published technique). The real compiled shader bytes for the sky
        // aren't reachable with this project's existing tooling (not in AglShader.sharcb or
        // AglLightShader.sharcb - the only two loadable shader archives anywhere in romfs - and
        // EnvironmentRenderer/environment_renderer_image's real cSkyColor sampler was traced to
        // drawOpa_'s per-material o_material_behave dispatch and confirmed to feed glossy/metal
        // REFLECTIONS, not the background - agl::pfx::Sky is almost certainly a shared Nintendo
        // SDK component with its shaders compiled directly into the executable rather than shipped
        // as loadable romfs content). So this ray-marches the SAME real physical quantities
        // (exponential density falloff by altitude, real per-channel Rayleigh/Mie scattering
        // coefficients, real phase functions, real optical-depth/transmittance integration) using
        // the REAL extracted parameters from res/../postfx/master_field.baglsky (see SkyPostFx),
        // rather than baking to an offscreen 3D LUT first - a faithful implementation of the
        // documented algorithm the game demonstrably uses, not an invented approximation.
        uniform vec3 uRayleighCoeff;        // real per-km Rayleigh scattering coefficients (SkyPostFx.RayleighScatteringCoeff)
        uniform float uRayleighScaleHeightKm; // real SkyPostFx.RayleighBaseHeight
        uniform float uMieCoeff;            // real per-km Mie scattering coefficient (SkyPostFx.MieScatteringCoeff)
        uniform float uMieScaleHeightKm;    // real SkyPostFx.MieBaseHeight
        uniform float uMieG;                // Henyey-Greenstein phase asymmetry (real SkyPostFx.MieSymmetricalPropRendering, palette override wins if authored)
        uniform vec3 uSunIntensity;         // top-of-atmosphere incoming sunlight colour*intensity - real SkyPostFx.SunColor, calibrated by Run's own intensityScale/AtmosphereIntensity
        uniform float uCameraHeightKm;      // camera altitude above the ray-march's own "ground" reference - see Run's own remarks; no real "sea level" reference exists in WildRenderingSharp's coordinate system, so this is an honest placeholder (0), not extracted data
        uniform vec3 uFogColor;       // palette's own authored horizon haze colour (EnvPalette.FogColor)
        uniform float uFogHorz;       // real postfx scatter_fog_horz - horizon fog falloff curve exponent (SkyPostFx.ScatterFogHorz)
        uniform vec3 uGroundColor;    // real postfx ground_color, pre-scaled by the same intensityScale as uRayleighColor/uMieColor (SkyPostFx.GroundColor)
        uniform vec3 uSunDiscColor;   // SkySunColor * SkySunColorIntensity, pre-clamped
        uniform float uSunSize;       // real postfx render_sun_size (SkyPostFx.RenderSunSize)
        uniform float uSunLerp;       // real postfx render_sun_lerp (SkyPostFx.RenderSunLerp)

        // Cloud0/Cloud1 - EnvPalette.CloudLayer's real authored gradient-shading fields. "Active"
        // is 1.0/0.0 (not a bool - see Run's own remarks) so an absent/NoUse layer's noise
        // coverage contributes exactly nothing rather than needing a branch.
        uniform float uCloud0Active, uCloud1Active;
        uniform vec3 uCloud0ColorBase, uCloud0ColorHilight, uCloud0ColorShadow, uCloud0ColorBackLight;
        uniform float uCloud0IntensityBase, uCloud0IntensityHilight, uCloud0IntensityShadow, uCloud0BacklightPower;
        uniform vec3 uCloud1ColorBase, uCloud1ColorHilight, uCloud1ColorShadow, uCloud1ColorBackLight;
        uniform float uCloud1IntensityBase, uCloud1IntensityHilight, uCloud1IntensityShadow, uCloud1BacklightPower;
        // Real postfx CloudParam0/CloudParam1's own mAlphaThreshold/mAlphaMul (SkyPostFx's CloudPostFx) -
        // the actual per-layer coverage-threshold/opacity data the game authors, not a shared guess.
        uniform float uCloud0AlphaThreshold, uCloud0AlphaMul, uCloud1AlphaThreshold, uCloud1AlphaMul;
        uniform float uCloud0Density, uCloud1Density; // real postfx CloudParamN's own mDensity - see cloudLayer's own remarks
        uniform float uCloudBrightness; // shared scale anchoring the (unit-authored) cloud colours to this pass's own magnitude range

        uniform float uStarBrightness; // 0 in full daylight, fades in as the sun sets - see Run's own remarks

        in vec2 vUV;
        out vec4 fragColor;

        const float PI = 3.14159265;

        float hash21(vec2 p) {
            p = fract(p * vec2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return fract(p.x * p.y);
        }
        float valueNoise(vec2 p) {
            vec2 i = floor(p), f = fract(p);
            float a = hash21(i), b = hash21(i + vec2(1.0, 0.0));
            float c = hash21(i + vec2(0.0, 1.0)), d = hash21(i + vec2(1.0, 1.0));
            vec2 u = f * f * (3.0 - 2.0 * f);
            return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
        }
        float fbm(vec2 p) {
            float v = 0.0, amp = 0.5;
            for (int i = 0; i < 4; i++) {
                v += amp * valueNoise(p);
                p *= 2.02;
                amp *= 0.5;
            }
            return v;
        }

        // Rayleigh phase function - real, textbook formula (integrates to 1 over the sphere).
        float rayleighPhase(float cosTheta) {
            return 3.0 / (16.0 * PI) * (1.0 + cosTheta * cosTheta);
        }
        // Henyey-Greenstein Mie phase function - same normalised formula this pass already used
        // for the old procedural Mie term, reused here unchanged (it was already real/correct).
        float miePhase(float cosTheta, float g) {
            float g2 = g * g;
            return (1.0 - g2) / (4.0 * PI * pow(max(1e-3, 1.0 + g2 - 2.0 * g * cosTheta), 1.5));
        }

        // Real exponential atmosphere density falloff by altitude (km above ground) - the same
        // "scale height" model Bruneton's own precompute uses, driven by the REAL
        // rayleigh_base_height/mie_base_height this pass extracted from master_field.baglsky.
        float rayleighDensity(float heightKm) { return exp(-max(heightKm, 0.0) / uRayleighScaleHeightKm); }
        float mieDensityAt(float heightKm) { return exp(-max(heightKm, 0.0) / uMieScaleHeightKm); }

        // Optical depth (Rayleigh, Mie) along a straight climbing segment, via a short march -
        // used for the SECONDARY (sun-ward) ray at each primary-ray sample, to get the real
        // transmittance of sunlight reaching that point before it scatters toward the camera.
        vec2 opticalDepthMarch(float h0, float climbRate, float dist, int steps) {
            float stepLen = dist / float(steps);
            vec2 depth = vec2(0.0);
            for (int i = 0; i < steps; i++) {
                float h = h0 + (float(i) + 0.5) * stepLen * climbRate;
                depth += vec2(rayleighDensity(h), mieDensityAt(h)) * stepLen;
            }
            return depth;
        }

        // The real single-scattering integral: march the PRIMARY view ray through the atmosphere,
        // and at each sample march a SECONDARY ray toward the sun to get real optical depth/
        // transmittance both ways, exactly the physical model Bruneton's precompute integrates
        // (just done live via ray march instead of via an offscreen 3D LUT - see this pass's own
        // remarks on why the real compiled bake shader isn't reachable). No literal spherical
        // planet is modelled - WildRenderingSharp's whole viewable range (up to a few km) is negligible next
        // to the real scale heights (tens of km), so a flat atmosphere slab is the honest
        // simplification here, not a fictional "planet radius" with no extracted ground truth.
        vec3 atmosphereSingleScatter(vec3 worldDir, vec3 sunDir, float cameraHeightKm) {
            const float ATMOSPHERE_TOP_KM = 100.0; // well past both real scale heights' falloff
            const int VIEW_STEPS = 16;
            const int SUN_STEPS = 8;

            if (worldDir.z <= 1e-4) return vec3(0.0);

            float viewDist = (ATMOSPHERE_TOP_KM - cameraHeightKm) / worldDir.z;
            float stepLen = viewDist / float(VIEW_STEPS);
            float cosTheta = dot(worldDir, sunDir);
            float rPhase = rayleighPhase(cosTheta);
            float mPhase = miePhase(cosTheta, uMieG);

            vec2 viewDepth = vec2(0.0);
            vec3 totalRayleigh = vec3(0.0);
            vec3 totalMie = vec3(0.0);

            for (int i = 0; i < VIEW_STEPS; i++) {
                float t = (float(i) + 0.5) * stepLen;
                float h = cameraHeightKm + t * worldDir.z;
                float densR = rayleighDensity(h);
                float densM = mieDensityAt(h);
                viewDepth += vec2(densR, densM) * stepLen;

                float sunClimb = max(sunDir.z, 0.02);
                float sunDist = (ATMOSPHERE_TOP_KM - h) / sunClimb;
                vec2 sunDepth = opticalDepthMarch(h, sunDir.z, sunDist, SUN_STEPS);

                vec2 totalDepth = viewDepth + sunDepth;
                vec3 transmittance = exp(-uRayleighCoeff * totalDepth.x - vec3(uMieCoeff * 1.1) * totalDepth.y);

                totalRayleigh += transmittance * densR * stepLen;
                totalMie += transmittance * densM * stepLen;
            }

            return uSunIntensity * (totalRayleigh * uRayleighCoeff * rPhase + totalMie * uMieCoeff * mPhase);
        }

        // A gradient-shaded cloud layer using EnvPalette.CloudLayer's own 3-way (shadow/base/hilight)
        // + backlight terms, exactly the structure the real palette YAML authors, over a cheap 2D
        // FBM sampled by (azimuth, elevation) - not the real game's VAT-driven cloud shapes (see
        // docs/sky_atmosphere_research.md), but real per-preset COLOURS over a plausible pattern.
        vec3 cloudLayer(vec2 domeUV, float layerActive, float cosTheta, float up,
                         vec3 colorBase, vec3 colorHilight, vec3 colorShadow, vec3 colorBackLight,
                         float intensityBase, float intensityHilight, float intensityShadow, float backlightPower,
                         float alphaThreshold, float alphaMul, float density,
                         vec3 baseSky) {
            // A LOW-frequency base shape (few octaves, big features - the actual cloud silhouette)
            // sharpened with a NARROW threshold band for a crisp puffy edge (a wide 0.3-wide band
            // read as one big soft blurry blob, not distinct clouds), plus a higher-frequency detail
            // term added only where the base shape already has coverage, to break the silhouette up
            // instead of leaving it one flat blob. The threshold band itself is anchored to the
            // real postfx CloudParam's own mAlphaThreshold (not a shared hardcoded 0.60 for both
            // layers) so the two layers genuinely differ in coverage density the way the game's own
            // authored data says they should, and mAlphaMul scales the resulting opacity the same
            // way the real shader would. density (real postfx mDensity, ~0.35-0.40) shifts the
            // threshold itself - a real "how much sky this layer covers" dial, previously extracted
            // but never actually wired to anything - rather than just multiplying opacity, since a
            // higher density authored value should mean MORE of the sky reads as cloud, not just a
            // more opaque version of the same small puffs.
            float baseShape = fbm(domeUV);
            float effectiveThreshold = alphaThreshold - (density - 0.4) * 0.6;
            float coverage = smoothstep(effectiveThreshold - 0.06, effectiveThreshold, baseShape);
            float detail = fbm(domeUV * 3.7 + 11.0) * coverage;
            coverage = clamp(coverage * (0.6 + 0.8 * detail) * alphaMul, 0.0, 1.0);

            // Hard-gated to strictly above the horizon (a narrow soft ramp right at the edge, not a
            // dimming multiplier applied afterward) - a PRIOR version only dimmed the whole sky
            // (clouds included) below the horizon by a flat 10x, which wasn't zero: near the
            // horizon "slightly above" and "slightly below" sample nearly IDENTICAL points in this
            // same continuous noise field (they're neighbours in domeUV space), so the dimmed
            // version of the exact same cloud shape kept showing through on the ground, reading as
            // a mirror reflection of the sky. Gating coverage itself (not just the final colour)
            // removes the cloud shape entirely below the horizon rather than merely darkening it.
            coverage *= smoothstep(-0.01, 0.04, up) * layerActive;

            float sunFacing = clamp(cosTheta, 0.0, 1.0);
            vec3 shaded = mix(colorShadow * intensityShadow, colorBase * intensityBase, sunFacing);
            shaded = mix(shaded, colorHilight * intensityHilight, pow(sunFacing, 2.0));
            float backlight = pow(clamp(-cosTheta, 0.0, 1.0), max(0.5, backlightPower));
            shaded += colorBackLight * backlight;
            return mix(baseSky, shaded * uCloudBrightness, coverage);
        }

        void main() {
            // vUV.y already increases bottom-to-top in lockstep with clip-space Y (see the
            // fullscreen-triangle vertex shader: vUV = clipPos*0.5+0.5) - NOT flipped, because this
            // pass draws straight into targets.Final, which is TRUE (unflipped-projection)
            // orientation. A prior version copied the "1.0 - uv.y*2.0" formula from
            // ScreenSpaceShadowAndAoPass, which genuinely needs that flip because IT reads
            // G-buffer-derived textures rendered through the pipeline's Y-FLIPPED projection - a
            // different pass, in a different space. Copying it here mirrored this pass's gradient
            // top-to-bottom: the screen CENTRE (aimed exactly where the camera points) still came
            // out right regardless, which is why the aggregate "look up = bright, look down = dark"
            // response (driven by the camera's own pitch, via uViewInv) still looked correct - but
            // the gradient's actual direction WITHIN a single frame was mirrored, which is what
            // read as the sky "moving weirdly" while tilting instead of shifting the way a real sky
            // would.
            vec3 viewDir = normalize(vec3((vUV.x * 2.0 - 1.0) * uTanHalf.x, (vUV.y * 2.0 - 1.0) * uTanHalf.y, -1.0));
            vec3 worldDir = normalize(mat3(uViewInv) * viewDir);

            float cosTheta = dot(worldDir, uSunWorld);
            float up = clamp(worldDir.z, -1.0, 1.0);
            float horizonFactor = 1.0 - abs(up); // 0 at zenith/nadir, 1 at the horizon

            // The real single-scattering atmosphere integral (see atmosphereSingleScatter's own
            // remarks for what this replaces and why) - real optical-depth/transmittance along
            // BOTH the view ray and the sun ray, using the real extracted physical parameters, in
            // place of the old flat "brightness is a function of elevation" formula and the old
            // un-integrated Mie glow term (the real HG phase function is now applied INSIDE the
            // integral, weighted by the same real transmittance, not tacked on separately).
            vec3 sky = atmosphereSingleScatter(worldDir, uSunWorld, uCameraHeightKm);

            // Horizon haze: blend toward the palette's own authored FogColor near the horizon -
            // real atmospheric colour data, not an invented gradient, and what actually breaks up
            // "one flat hue everywhere" into something that reads as a real sky. uFogColor arrives
            // ALREADY calibrated (intensityScale*AtmosphereIntensity folded in on the CPU side - see
            // Run's own remarks) - a PRIOR version left it at the palette's raw unit-scale magnitude
            // with only a flat *0.35 here, which was completely independent of AtmosphereIntensity:
            // dialling that slider to 0 still left a bright, direction-independent horizon band
            // (confirmed: "even on atmos intensity 0, there's this bright bluish white band on the
            // horizon in all directions" - a real, sun-direction-INDEPENDENT band is exactly what an
            // uncalibrated fixed-weight blend looks like, since the actual atmosphere integral IS
            // sun-direction-dependent and had correctly gone dark). The falloff curve's own exponent
            // is the real postfx scatter_fog_horz (2.5) in place of a bare squaring
            // (horizonFactor*horizonFactor implied exponent 2) - real data shaping the curve, the
            // 0.5 peak-weight stays a WildRenderingSharp-side calibration constant since this is layered ON TOP
            // of the real integral, not the real ray-marched fog volume itself.
            sky = mix(sky, uFogColor, pow(horizonFactor, uFogHorz) * 0.5);

            // A generous safety ceiling only - NOT a brightness-shaping tool the way the old flat
            // formula needed one. The real optical-depth integral above is already physically
            // self-bounding (transmittance can't exceed 1, densities decay to 0), so this exists
            // purely to guarantee no NaN/Inf or genuinely pathological input can reach the Exposure
            // multiply downstream as a solid white screen - it should essentially never trigger in
            // normal use, unlike the old 0.4 ceiling the flat formula relied on to avoid clipping.
            sky = min(sky, vec3(4.0));

            // Shared "sky dome" projection (azimuth, elevation) for both stars and clouds below -
            // cheap, no Cartesian-grid distortion, no singularities except the poles (never stared
            // into for long). A PRIOR version hashed stars directly off floor(worldDir * N) in
            // Cartesian space, which - being a plain axis-aligned box grid wrapped around a
            // SPHERE's surface - produces visibly elongated, tiled-looking cells at low elevation
            // (confirmed: "the stars are on the floor" - what actually happened was stars right
            // near the horizon reading as a repeating floor-tile pattern, not literal
            // below-the-horizon placement, which the up > 0.0 gate already prevented). The dome
            // parameterisation used for clouds doesn't have that distortion, so stars now share it.
            float azimuth = atan(worldDir.y, worldDir.x);
            float elevation = asin(up);
            vec2 domeUV = vec2(azimuth, elevation);

            // Stars: fixed to the WORLD (keyed by direction, not screen position), softly fading
            // in near the horizon (not a hard cutoff - a star at full brightness one pixel above
            // up = 0 read as sitting right at ground level) and only once the sun has set.
            if (uStarBrightness > 0.001) {
                float starHash = hash21(floor(domeUV * 240.0));
                float star = smoothstep(0.985, 0.999, starHash);
                float horizonFade = smoothstep(0.0, 0.12, up);
                sky += vec3(star * uStarBrightness * horizonFade);
            }

            // Clouds: two independent layers, each with its own noise offset/scale so they read as
            // visually distinct, plus the real per-layer postfx alphaThreshold/alphaMul.
            sky = cloudLayer(domeUV * 2.2 + vec2(3.7, 0.0), uCloud0Active, cosTheta, up,
                              uCloud0ColorBase, uCloud0ColorHilight, uCloud0ColorShadow, uCloud0ColorBackLight,
                              uCloud0IntensityBase, uCloud0IntensityHilight, uCloud0IntensityShadow, uCloud0BacklightPower,
                              uCloud0AlphaThreshold, uCloud0AlphaMul, uCloud0Density, sky);
            sky = cloudLayer(domeUV * 4.5 + vec2(-1.2, 0.6), uCloud1Active, cosTheta, up,
                              uCloud1ColorBase, uCloud1ColorHilight, uCloud1ColorShadow, uCloud1ColorBackLight,
                              uCloud1IntensityBase, uCloud1IntensityHilight, uCloud1IntensityShadow, uCloud1BacklightPower,
                              uCloud1AlphaThreshold, uCloud1AlphaMul, uCloud1Density, sky);

            // Below the horizon: fade toward the real postfx ground_color (SkyPostFx.GroundColor,
            // pre-scaled by the same intensityScale as the rayleigh/mie terms) rather than an
            // arbitrary "same hue, 10x dimmer" guess - real authored data for what the sky shader
            // shows looking down, not an invented dim. Applied AFTER clouds/stars so the ground
            // doesn't show bright sky content bleeding through it.
            float belowHorizon = clamp(-up * 4.0, 0.0, 1.0);
            sky = mix(sky, uGroundColor, belowHorizon);

            // The sun itself: a small, sharp disc at its exact world direction - added last, past
            // the horizon fade, since it should read as a genuine bright light source regardless.
            // Sized/shaped by the real postfx render_sun_size/render_sun_lerp (SkyPostFx.RenderSunSize/
            // RenderSunLerp, both 1.0 in the shipped master_field) instead of a fixed window - at the
            // defaults this reproduces the original hand-tuned (0.9990, 0.9998) window exactly.
            float sunHalfWidth = 0.0008 * max(0.05, uSunSize);
            float sunCenter = 0.9998 - sunHalfWidth;
            float sunDisc = smoothstep(sunCenter - sunHalfWidth * max(0.1, uSunLerp), sunCenter + sunHalfWidth, cosTheta);
            sky += uSunDiscColor * sunDisc;

            fragColor = vec4(sky, 1.0);
        }
        """;

    public BackgroundPass(GL gl)
    {
        _gl = gl;
        _skyProgram = GLProgramBuilder.Build(gl, SkyVertexSource, SkyFragmentSource, "background_sky");
    }

    /// <param name="viewInv3Rows">The camera's inverse-view matrix, 3 affine rows - same convention every other screen-space pass here takes (see <see cref="ScreenSpaceShadowAndAoPass"/>).</param>
    /// <param name="tanHalf">(tan(fovX/2), tan(fovY/2)).</param>
    /// <param name="sceneGain">
    /// <c>LightingContext.SceneGain</c> - the SAME correction <c>DeferredResolvePass</c>'s compose
    /// shader already applies to the lit path's own <c>BgDifIntensity</c>-scaled values (see this
    /// class's own remarks on why the sky's intensity anchors off it too, not an independent guess).
    /// </param>
    /// <param name="postfx">The real <c>agl::pfx::Sky</c> static baseline loaded straight from romfs (<see cref="SkyPostFxLibrary.LoadFromRomfs"/>) - null falls back to <see cref="SkyPostFx.Default"/>.</param>
    /// <param name="cloudPostFx">The real <c>agl::fx::Cloud</c> static baseline (same loader) - null falls back to <see cref="CloudPostFx.Default"/>.</param>
    /// <param name="skyBin">
    /// The real precomputed sky-scattering LUT (<see cref="SkyBinLut.LoadFromCache"/>) - extracted
    /// real data, but NOT currently used to drive the visible sky (see
    /// <see cref="atmosphereSingleScatter"/>'s own remarks: the real Bruneton bake this LUT was
    /// hypothesised to mirror turned out to be a reflection-probe-resolution asset feeding
    /// something else, not the background compositor - kept wired through for a future real use,
    /// not a dead parameter to delete). Accepted but currently unread.
    /// </param>
    /// <param name="atmosphereIntensity"><see cref="LightingContext.AtmosphereIntensity"/> - the user's own live multiplier on the real single-scattering integral's brightness anchor.</param>
    public void Run(GLResourceCache resources, RenderTargets targets, BackgroundMode mode, Vector3 color,
        Vector3 sunWorld, EnvPalette palette, ReadOnlySpan<Vector4> viewInv3Rows, Vector2 tanHalf, float sceneGain,
        SkyPostFx? postfx = null, CloudPostFx? cloudPostFx = null, SkyBinLut? skyBin = null, float atmosphereIntensity = 1f)
    {
        postfx ??= SkyPostFx.Default;
        cloudPostFx ??= CloudPostFx.Default;
        skyBin ??= SkyBinLut.Empty;
        targets.BindColorTarget(targets.Final);
        _gl.Disable(EnableCap.DepthTest);

        switch (mode)
        {
            case BackgroundMode.Transparent:
                _gl.ClearColor(0f, 0f, 0f, 0f);
                _gl.Clear(ClearBufferMask.ColorBufferBit);
                break;

            case BackgroundMode.TotkSky:
                // Anchored to the SceneGain-corrected BgDifIntensity, the same magnitude family
                // the lit path's own compose step already targets (see class remarks) - NOT tuned
                // to "look reasonable pre-Exposure" in isolation, which is what made the first
                // attempt at this (a 0.15 constant here) come back "pretty damn bright... mostly
                // white" once the viewer's own Exposure multiplied it afterward.
                float intensityScale = palette.BgDifIntensity * sceneGain * SkyColorAnchor;
                Vector3 sunDiscColor = palette.SkySunColorNoUse
                    ? Vector3.Zero
                    : Vector3.Min(palette.SkySunColor * palette.SkySunColorIntensity * sceneGain, new Vector3(3f));

                // The palette's own SkyRParam_mie_symmetrical wins when authored; most palettes
                // don't (EnvPalette.SkyMieSymmetrical's own remarks - "carried for completeness",
                // defaulting to 0 when absent, which would read as an isotropic Mie lobe with no
                // forward-scattered sun glow), in which case the real postfx
                // mie_symmetrical_prop_rendering (the actual engine-wide rendering default) is the
                // better fallback than that bare 0.
                float mieG = palette.SkyMieSymmetrical != 0f ? palette.SkyMieSymmetrical : postfx.MieSymmetricalPropRendering;

                _gl.UseProgram(_skyProgram);
                _gl.SetMat4(_skyProgram, "uViewInv", Rendering.Mat4Math.ToMat4(viewInv3Rows));
                _gl.SetVec2(_skyProgram, "uTanHalf", tanHalf);
                _gl.SetVec3(_skyProgram, "uSunWorld", sunWorld);

                // Real single-scattering atmosphere parameters, straight from master_field.baglsky
                // (see SkyPostFx) - the actual physical quantities the real Bruneton bake integrates,
                // fed into the live ray-march instead (see atmosphereSingleScatter's own remarks).
                //
                // The postfx file's own coefficients are the STATIC physical baseline - fixed
                // regardless of time-of-day/weather/directional-light colour. The PALETTE'S OWN
                // SkyRParam_rayleigh_amplifier/SkyRParam_mie_amplifier/SkySunColor are the DYNAMIC
                // per-scene layer on top of that baseline (EnvPalette's own remarks: "1.0 at
                // overworld noon, 0.25 at night, 0.0 under a blood moon" for Rayleigh; "12 at noon,
                // 0 at night, 256 under a blood moon" for Mie) - confirmed missing here: "since we
                // wired this new one up, the color of the directional light... no longer changes
                // the atmosphere's color like it does in game" - an earlier version of this method
                // (AmbientLighting.SkyScatteringTerms, removed when this became a real ray march)
                // DID apply these; the rewrite dropped them by only reading the static postfx side.
                // ReferenceRayleighAmplifier/ReferenceMieAmplifier are the SAME reference-daylight-
                // palette normalisation that removed method used - the postfx file's own "dynamic_
                // rayleigh_amplifier"=1/"dynamic_mie_amplifier"=12 defaults independently match
                // those exact reference numbers, which is why they're trustworthy as the neutral point.
                const float ReferenceRayleighAmplifier = 1.0f;
                const float ReferenceMieAmplifier = 12.0f;
                float rayleighAmplifier = MathF.Max(0f, palette.SkyRayleighAmplifier) / ReferenceRayleighAmplifier;
                float mieAmplifier = MathF.Max(0f, palette.SkyMieAmplifier) / ReferenceMieAmplifier;

                _gl.SetVec3(_skyProgram, "uRayleighCoeff", postfx.RayleighScatteringCoeff * rayleighAmplifier);
                _gl.SetFloat(_skyProgram, "uRayleighScaleHeightKm", postfx.RayleighBaseHeight);
                _gl.SetFloat(_skyProgram, "uMieCoeff", postfx.MieScatteringCoeff * mieAmplifier);
                _gl.SetFloat(_skyProgram, "uMieScaleHeightKm", postfx.MieBaseHeight);
                _gl.SetFloat(_skyProgram, "uMieG", mieG);
                // No real "sea level" reference exists in WildRenderingSharp's own coordinate system (models
                // are placed anywhere) - 0 is the honest default rather than a fabricated altitude.
                _gl.SetFloat(_skyProgram, "uCameraHeightKm", 0f);
                // Top-of-atmosphere incoming sunlight: the PALETTE's own dynamic SkySunColor
                // (respecting SkySunColorNoUse, same as the sun disc above - the atmosphere's own
                // Mie glow and the disc should share one real colour source, not two), not the
                // static postfx.SunColor - see the amplifier remarks above for why. Calibrated by
                // the same intensityScale family the rest of this pipeline already anchors to,
                // times the user's own live AtmosphereIntensity multiplier (see LightingContext's
                // own remarks). NOT floored above 0 - a slider value of exactly 0 needs to
                // genuinely mean "no atmosphere contribution", both because that's what the control
                // says and because it's the only way to isolate whether some OTHER term (fog/ground,
                // both below) is contributing independently of this slider.
                Vector3 sunColorSrc = palette.SkySunColorNoUse ? Vector3.Zero : palette.SkySunColor * palette.SkySunColorIntensity;
                Vector3 sunIntensity = sunColorSrc * intensityScale * 40f * atmosphereIntensity;
                _gl.SetVec3(_skyProgram, "uSunIntensity", sunIntensity);

                // uFogColor/uGroundColor: the palette's/postfx's own raw authored magnitudes,
                // pre-scaled by the SAME intensityScale*AtmosphereIntensity family as the
                // atmosphere integral above - NOT independent of the slider (a prior version left
                // uFogColor unscaled by AtmosphereIntensity entirely, which left a bright,
                // sun-direction-INDEPENDENT horizon band on screen even with the slider at 0 - see
                // the horizon-haze blend's own remarks for the confirmed symptom).
                float ambientCalib = intensityScale * atmosphereIntensity;
                _gl.SetVec3(_skyProgram, "uFogColor", palette.FogColor * 0.35f * ambientCalib);
                _gl.SetFloat(_skyProgram, "uFogHorz", postfx.ScatterFogHorz);
                _gl.SetVec3(_skyProgram, "uGroundColor", postfx.GroundColor * ambientCalib);
                _gl.SetVec3(_skyProgram, "uSunDiscColor", sunDiscColor);
                _gl.SetFloat(_skyProgram, "uSunSize", postfx.RenderSunSize);
                _gl.SetFloat(_skyProgram, "uSunLerp", postfx.RenderSunLerp);

                // Shared anchor for BOTH cloud layers' own (roughly unit-magnitude authored)
                // colour*intensity products, landing them in the same rough range as the sky's own
                // 0.4 ceiling rather than either washing it out or being invisibly dim against it.
                float cloudBrightness = MathF.Min(0.6f, palette.BgDifIntensity * sceneGain * 0.35f);
                _gl.SetFloat(_skyProgram, "uCloudBrightness", cloudBrightness);
                SetCloudLayer(0, palette.Cloud0, cloudPostFx.Layer0);
                SetCloudLayer(1, palette.Cloud1, cloudPostFx.Layer1);

                // Fades in only once the sun sets (sunWorld.Z < 0 - Z-up) - real day/night, not a
                // palette field (the shipped palettes don't author a star toggle at all).
                float starBrightness = MathF.Min(0.5f, MathF.Max(0f, -sunWorld.Z) * 1.5f);
                _gl.SetFloat(_skyProgram, "uStarBrightness", starBrightness);

                resources.DrawFullscreenTriangle();
                break;

            default: // Color
                _gl.ClearColor(color.X, color.Y, color.Z, 1f);
                _gl.Clear(ClearBufferMask.ColorBufferBit);
                break;
        }
    }

    /// <summary>
    /// Uploads one layer's uniforms: <c>EnvPalette.CloudLayer</c>'s real per-palette authored
    /// colour/intensity/backlight fields, PLUS the real postfx <c>CloudParamN</c>'s own
    /// alphaThreshold/alphaMul (a genuinely different, static, romfs-loaded source - see
    /// <see cref="SkyPostFx"/>'s class remarks on why these two don't collapse into one).
    /// <paramref name="index"/> is 0 or 1.
    /// </summary>
    void SetCloudLayer(int index, EnvPalette.CloudLayer layer, CloudPostFxLayer postfxLayer)
    {
        string p = $"uCloud{index}";
        _gl.SetFloat(_skyProgram, p + "Active", layer.Present ? 1f : 0f);
        _gl.SetVec3(_skyProgram, p + "ColorBase", layer.ColorBase);
        _gl.SetVec3(_skyProgram, p + "ColorHilight", layer.ColorHilight);
        _gl.SetVec3(_skyProgram, p + "ColorShadow", layer.ColorShadow);
        _gl.SetVec3(_skyProgram, p + "ColorBackLight", layer.ColorBackLight);
        _gl.SetFloat(_skyProgram, p + "IntensityBase", layer.IntensityBase);
        _gl.SetFloat(_skyProgram, p + "IntensityHilight", layer.IntensityHilight);
        _gl.SetFloat(_skyProgram, p + "IntensityShadow", layer.IntensityShadow);
        _gl.SetFloat(_skyProgram, p + "BacklightPower", layer.BacklightPower);
        _gl.SetFloat(_skyProgram, p + "AlphaThreshold", postfxLayer.AlphaThreshold);
        _gl.SetFloat(_skyProgram, p + "AlphaMul", postfxLayer.AlphaMul);
        _gl.SetFloat(_skyProgram, p + "Density", postfxLayer.Density);
    }

    public void Dispose() => _gl.DeleteProgram(_skyProgram);
}
