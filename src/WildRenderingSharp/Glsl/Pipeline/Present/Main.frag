#version 450 core
uniform sampler2D t; uniform int uMode; uniform int uSS; uniform vec2 uTexel;
uniform float uSaturation; uniform float uBrightness; uniform float uGamma; uniform float uRawScale;
uniform sampler2D tAlpha; uniform int uUseAlpha;
in vec2 vUV; out vec4 f;
void main() {
    // Mode 2: a diagnostic passthrough for near-zero buffers (the pass-ID mask, one PreShadow or PreMisc channel): no supersample averaging or grading, just scaled so small values are
    // distinguishable. Also the plain "no AA" blit of an already-graded image, where the source's alpha must survive (Background: Transparent; see alphaSource).
    if (uMode == 2) {
        float a2 = uUseAlpha == 1 ? texture(tAlpha, vUV).a : 1.0;
        f = vec4(texture(t, vUV).rgb * uRawScale, a2);
        return;
    }

    vec3 c = vec3(0.0);
    float alphaSum = 0.0;
    float valid_samples = 0.0;
    for (int y = 0; y < uSS; ++y) {
        for (int x = 0; x < uSS; ++x) {
            vec2 off = (vec2(x, y) - 0.5 * float(uSS - 1)) * uTexel;
            vec3 s = texture(t, vUV + off).rgb;
            if (!isnan(s.r) && !isnan(s.g) && !isnan(s.b) && !isinf(s.r) && !isinf(s.g) && !isinf(s.b)) {
                c += s;
                valid_samples += 1.0;
            }
            if (uUseAlpha == 1) alphaSum += texture(tAlpha, vUV + off).a;
        }
    }
    if (valid_samples > 0.0) c /= valid_samples;
    if (uMode == 1) c = max(c, 0.0) / (1.0 + max(c, 0.0));

    float luma = dot(c, vec3(0.2989, 0.5866, 0.1144));
    c = luma + (c - luma) * uSaturation;
    c = clamp(c * uBrightness, 0.0, 1.0);
    if (uGamma != 1.0) c = pow(c, vec3(1.0 / uGamma));

    c = clamp(c, 0.0, 1.0);
    float outAlpha = uUseAlpha == 1 ? clamp(alphaSum / float(uSS * uSS), 0.0, 1.0) : 1.0;
    f = vec4(mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c)), outAlpha);
}
