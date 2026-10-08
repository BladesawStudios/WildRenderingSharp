#version 330 core
uniform sampler2D uImage;
uniform sampler2D uDepth;
uniform vec2 uNearFar;
uniform vec4 uProjDepth;    // the viewer's projection: M33, M43, M34, M44
uniform int uReversed;
uniform int uSky;
uniform int uFlip;
uniform float uClear;
in vec2 vUV;
out vec4 fragColor;
vec3 toLinear(vec3 c) { return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c)); }
vec3 toSrgb(vec3 c) { return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c)); }
void main() {
    // The renderer's depth is stored the G-buffer's way up - upside down to its colour.
    ivec2 size = textureSize(uDepth, 0);
    ivec2 at = clamp(ivec2(vec2(vUV.x, 1.0 - vUV.y) * vec2(size)), ivec2(0), size - 1);
    float d = texelFetch(uDepth, at, 0).r;
    vec4 c = texture(uImage, uFlip == 1 ? vec2(vUV.x, 1.0 - vUV.y) : vUV);

    if (d >= 1.0) {
        // Nothing of the renderer's here: its sky if it drew one, at the far plane so the
        // ground drawn next covers it, otherwise left to the viewer.
        if (uSky == 0 || c.a <= 0.0) discard;
        gl_FragDepth = uClear;
        fragColor = vec4(clamp(toSrgb(toLinear(c.rgb) / max(c.a, 1e-4)), 0.0, 1.0), 1.0);
        return;
    }

    // Its depth back to a distance, then into the viewer's own convention.
    float n = uNearFar.x, f = uNearFar.y;
    float viewZ = (2.0 * n * f) / (f + n - (d * 2.0 - 1.0) * (f - n));
    float clipZ = -viewZ * uProjDepth.x + uProjDepth.y;
    float clipW = -viewZ * uProjDepth.z + uProjDepth.w;
    float ndc = clipZ / clipW;
    gl_FragDepth = uReversed == 1 ? ndc : ndc * 0.5 + 0.5;

    float a = max(c.a, 1e-4);
    fragColor = vec4(clamp(toSrgb(toLinear(c.rgb) / a), 0.0, 1.0), 1.0);
}