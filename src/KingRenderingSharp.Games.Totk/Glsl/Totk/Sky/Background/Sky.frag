#version 450 core
uniform mat4 uViewInv;
uniform vec2 uTanHalf;        // (tanHalfFovX, tanHalfFovY)
uniform vec3 uSunWorld;       // direction TOWARD the sun, world space, normalized

// Single-scattering atmosphere (Rayleigh + Mie), ray-marched per pixel with the parameters
// from master_field.baglsky (see SkyPostFx).
uniform vec3 uRayleighCoeff; // per-km Rayleigh scattering coefficients
uniform float uRayleighScaleHeightKm; // Rayleigh scale height
uniform float uMieCoeff; // per-km Mie scattering coefficient
uniform float uMieScaleHeightKm; // Mie scale height
uniform float uMieG; // Henyey-Greenstein asymmetry
uniform vec3 uSunIntensity; // top-of-atmosphere sunlight, colour * intensity
uniform float uCameraHeightKm; // camera altitude above the march's ground reference (0: this coordinate system has no sea level)
uniform vec3 uFogColor; // palette horizon haze colour
uniform float uFogHorz; // horizon fog falloff exponent
uniform vec3 uGroundColor; // ground colour, pre-scaled like the scattering terms
uniform vec3 uSunDiscColor; // sun disc colour
uniform float uSunSize; // sun disc size
uniform float uSunLerp; // sun disc edge softness

// Cloud0/Cloud1 shading fields from the palette. "Active" is 1.0 or 0.0 rather than a bool so
// an absent layer contributes nothing without a branch.
uniform float uCloud0Active, uCloud1Active;
uniform vec3 uCloud0ColorBase, uCloud0ColorHilight, uCloud0ColorShadow, uCloud0ColorBackLight;
uniform float uCloud0IntensityBase, uCloud0IntensityHilight, uCloud0IntensityShadow, uCloud0BacklightPower;
uniform vec3 uCloud1ColorBase, uCloud1ColorHilight, uCloud1ColorShadow, uCloud1ColorBackLight;
uniform float uCloud1IntensityBase, uCloud1IntensityHilight, uCloud1IntensityShadow, uCloud1BacklightPower;
// Per-layer coverage threshold and opacity from the postfx CloudParam blocks.
uniform float uCloud0AlphaThreshold, uCloud0AlphaMul, uCloud1AlphaThreshold, uCloud1AlphaMul;
uniform float uCloud0Density, uCloud1Density; // postfx density; shifts the coverage threshold
uniform float uCloudBrightness; // anchors the unit-scale cloud colours to this pass's magnitude

uniform float uStarBrightness; // 0 in daylight, fades in as the sun sets

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

// Rayleigh phase function.
float rayleighPhase(float cosTheta) {
    return 3.0 / (16.0 * PI) * (1.0 + cosTheta * cosTheta);
}
// Henyey-Greenstein Mie phase function.
float miePhase(float cosTheta, float g) {
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * PI * pow(max(1e-3, 1.0 + g2 - 2.0 * g * cosTheta), 1.5));
}

// Exponential density falloff by altitude (km), driven by the postfx scale heights.
float rayleighDensity(float heightKm) { return exp(-max(heightKm, 0.0) / uRayleighScaleHeightKm); }
float mieDensityAt(float heightKm) { return exp(-max(heightKm, 0.0) / uMieScaleHeightKm); }

// Optical depth (Rayleigh, Mie) along a climbing segment, for the sun-ward ray at each view sample.
vec2 opticalDepthMarch(float h0, float climbRate, float dist, int steps) {
    float stepLen = dist / float(steps);
    vec2 depth = vec2(0.0);
    for (int i = 0; i < steps; i++) {
        float h = h0 + (float(i) + 0.5) * stepLen * climbRate;
        depth += vec2(rayleighDensity(h), mieDensityAt(h)) * stepLen;
    }
    return depth;
}

// The single-scattering integral: march the view ray and, at each sample, a ray toward the sun.
// A flat slab stands in for the planet because the viewable range is negligible next to the scale heights.
vec3 atmosphereSingleScatter(vec3 worldDir, vec3 sunDir, float cameraHeightKm) {
    const float ATMOSPHERE_TOP_KM = 100.0; // well past both real scale heights' falloff
    const int VIEW_STEPS = 16;
    const int SUN_STEPS = 8;

    if (worldDir.y <= 1e-4) return vec3(0.0);

    float viewDist = (ATMOSPHERE_TOP_KM - cameraHeightKm) / worldDir.y;
    float stepLen = viewDist / float(VIEW_STEPS);
    float cosTheta = dot(worldDir, sunDir);
    float rPhase = rayleighPhase(cosTheta);
    float mPhase = miePhase(cosTheta, uMieG);

    vec2 viewDepth = vec2(0.0);
    vec3 totalRayleigh = vec3(0.0);
    vec3 totalMie = vec3(0.0);

    for (int i = 0; i < VIEW_STEPS; i++) {
        float t = (float(i) + 0.5) * stepLen;
        float h = cameraHeightKm + t * worldDir.y;
        float densR = rayleighDensity(h);
        float densM = mieDensityAt(h);
        viewDepth += vec2(densR, densM) * stepLen;

        float sunClimb = max(sunDir.y, 0.02);
        float sunDist = (ATMOSPHERE_TOP_KM - h) / sunClimb;
        vec2 sunDepth = opticalDepthMarch(h, sunDir.y, sunDist, SUN_STEPS);

        vec2 totalDepth = viewDepth + sunDepth;
        vec3 transmittance = exp(-uRayleighCoeff * totalDepth.x - vec3(uMieCoeff * 1.1) * totalDepth.y);

        totalRayleigh += transmittance * densR * stepLen;
        totalMie += transmittance * densM * stepLen;
    }

    return uSunIntensity * (totalRayleigh * uRayleighCoeff * rPhase + totalMie * uMieCoeff * mPhase);
}

// A gradient-shaded cloud layer over cheap 2D FBM, using the palette's shadow, base, hilight and
// backlight terms. The shapes are a stand-in; the colours are authored.
vec3 cloudLayer(vec2 domeUV, float layerActive, float cosTheta, float up,
                 vec3 colorBase, vec3 colorHilight, vec3 colorShadow, vec3 colorBackLight,
                 float intensityBase, float intensityHilight, float intensityShadow, float backlightPower,
                 float alphaThreshold, float alphaMul, float density,
                 vec3 baseSky) {
    // A low-frequency base shape sharpened by a narrow threshold band, plus higher-frequency
    // detail where the base has coverage. The threshold follows the postfx alphaThreshold and
    // density, so the layers differ in coverage as authored; alphaMul scales the opacity.
    float baseShape = fbm(domeUV);
    float effectiveThreshold = alphaThreshold - (density - 0.4) * 0.6;
    float coverage = smoothstep(effectiveThreshold - 0.06, effectiveThreshold, baseShape);
    float detail = fbm(domeUV * 3.7 + 11.0) * coverage;
    coverage = clamp(coverage * (0.6 + 0.8 * detail) * alphaMul, 0.0, 1.0);

    // Coverage itself is gated below the horizon: only dimming it left the same cloud shape
    // showing through as a mirrored sky.
    coverage *= smoothstep(-0.01, 0.04, up) * layerActive;

    float sunFacing = clamp(cosTheta, 0.0, 1.0);
    vec3 shaded = mix(colorShadow * intensityShadow, colorBase * intensityBase, sunFacing);
    shaded = mix(shaded, colorHilight * intensityHilight, pow(sunFacing, 2.0));
    float backlight = pow(clamp(-cosTheta, 0.0, 1.0), max(0.5, backlightPower));
    shaded += colorBackLight * backlight;
    return mix(baseSky, shaded * uCloudBrightness, coverage);
}

void main() {
    // vUV.y already rises with clip-space Y and this pass draws into the unflipped Final, so no flip is applied.
    vec3 viewDir = normalize(vec3((vUV.x * 2.0 - 1.0) * uTanHalf.x, (vUV.y * 2.0 - 1.0) * uTanHalf.y, -1.0));
    vec3 worldDir = normalize(mat3(uViewInv) * viewDir);

    float cosTheta = dot(worldDir, uSunWorld);
    float up = clamp(worldDir.y, -1.0, 1.0);
    float horizonFactor = 1.0 - abs(up); // 0 at zenith/nadir, 1 at the horizon

    // The single-scattering integral, with the phase functions applied inside it.
    vec3 sky = atmosphereSingleScatter(worldDir, uSunWorld, uCameraHeightKm);

    // Horizon haze toward the palette fog colour. uFogColor arrives pre-scaled by the atmosphere
    // intensity, so the slider at 0 removes the band; the 0.5 peak weight is a calibration
    // because this sits on top of the integral.
    sky = mix(sky, uFogColor, pow(horizonFactor, uFogHorz) * 0.5);

    // A ceiling against NaN or pathological input reaching the exposure multiply; the integral is self-bounding.
    sky = min(sky, vec3(4.0));

    // A shared (azimuth, elevation) dome projection for stars and clouds; a Cartesian grid would tile visibly near the horizon.
    float azimuth = atan(-worldDir.z, worldDir.x);
    float elevation = asin(up);
    vec2 domeUV = vec2(azimuth, elevation);

    // Stars are fixed to the world, fade in near the horizon, and appear once the sun has set.
    if (uStarBrightness > 0.001) {
        float starHash = hash21(floor(domeUV * 240.0));
        float star = smoothstep(0.985, 0.999, starHash);
        float horizonFade = smoothstep(0.0, 0.12, up);
        sky += vec3(star * uStarBrightness * horizonFade);
    }

    // Two independent cloud layers, each with its own noise offset and scale.
    sky = cloudLayer(domeUV * 2.2 + vec2(3.7, 0.0), uCloud0Active, cosTheta, up,
                      uCloud0ColorBase, uCloud0ColorHilight, uCloud0ColorShadow, uCloud0ColorBackLight,
                      uCloud0IntensityBase, uCloud0IntensityHilight, uCloud0IntensityShadow, uCloud0BacklightPower,
                      uCloud0AlphaThreshold, uCloud0AlphaMul, uCloud0Density, sky);
    sky = cloudLayer(domeUV * 4.5 + vec2(-1.2, 0.6), uCloud1Active, cosTheta, up,
                      uCloud1ColorBase, uCloud1ColorHilight, uCloud1ColorShadow, uCloud1ColorBackLight,
                      uCloud1IntensityBase, uCloud1IntensityHilight, uCloud1IntensityShadow, uCloud1BacklightPower,
                      uCloud1AlphaThreshold, uCloud1AlphaMul, uCloud1Density, sky);

    // Below the horizon, fade to the postfx ground colour. Applied after clouds and stars so they do not bleed through.
    float belowHorizon = clamp(-up * 4.0, 0.0, 1.0);
    sky = mix(sky, uGroundColor, belowHorizon);

    // The sun disc, added last so it reads as a light source regardless of the horizon fade.
    // Its size and edge come from the postfx; the defaults reproduce the original window.
    float sunHalfWidth = 0.0008 * max(0.05, uSunSize);
    float sunCenter = 0.9998 - sunHalfWidth;
    float sunDisc = smoothstep(sunCenter - sunHalfWidth * max(0.1, uSunLerp), sunCenter + sunHalfWidth, cosTheta);
    sky += uSunDiscColor * sunDisc;

    fragColor = vec4(sky, 1.0);
}
