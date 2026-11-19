#version 330 core
in vec2 vUV;
uniform sampler2D tScene;
uniform float uHue, uSaturation, uBrightness, uGamma;
uniform int uToycamEnable, uOrderToycamHsb;
uniform vec3 uOffset1, uOffset2, uLevel1, uLevel2, uMulColor;
uniform float uSat1, uSat2, uToyBrightness, uToyContrast;
out vec4 oCol;

const vec3 LUMA = vec3(0.2126, 0.7152, 0.0722);

vec3 applySaturation(vec3 c, float s)
{
    return mix(vec3(dot(c, LUMA)), c, s);
}

vec3 applyHue(vec3 c, float radians)
{
    if (abs(radians) < 1e-5) return c;
    // Rotation about the luma axis in RGB: equivalent to an HSV hue shift without the discontinuity at the hue wrap that would band a smooth sky gradient.
    float cosA = cos(radians), sinA = sin(radians);
    float k = 1.0 / 3.0, sq = sqrt(k);
    mat3 m = mat3(
        cosA + (1.0 - cosA) * k,        k * (1.0 - cosA) - sq * sinA, k * (1.0 - cosA) + sq * sinA,
        k * (1.0 - cosA) + sq * sinA,   cosA + k * (1.0 - cosA),      k * (1.0 - cosA) - sq * sinA,
        k * (1.0 - cosA) - sq * sinA,   k * (1.0 - cosA) + sq * sinA, cosA + k * (1.0 - cosA));
    return m * c;
}

vec3 applyToycam(vec3 c)
{
    // Two lift/gain stages with their own saturation, then contrast about mid grey and a colour multiply: the shape agl's toycam parameters describe.
    c = applySaturation(c * uLevel1 + uOffset1, uSat1);
    c = applySaturation(c * uLevel2 + uOffset2, uSat2);
    c = (c - 0.5) * uToyContrast + 0.5;
    return c * uToyBrightness * uMulColor;
}

void main()
{
    vec3 c = texture(tScene, vUV).rgb;

    if (uToycamEnable == 1 && uOrderToycamHsb == 1)
        c = applyToycam(c);

    c = applyHue(c, uHue);
    c = applySaturation(c, uSaturation);
    c *= uBrightness;

    if (uToycamEnable == 1 && uOrderToycamHsb == 0)
        c = applyToycam(c);

    // Gamma last and guarded: this runs on a tonemapped image, but a saturation boost can push a channel below zero, and pow() of a negative is undefined (black speckle).
    if (abs(uGamma - 1.0) > 1e-5)
        c = pow(max(c, vec3(0.0)), vec3(1.0 / max(uGamma, 1e-4)));

    oCol = vec4(c, 1.0);
}
