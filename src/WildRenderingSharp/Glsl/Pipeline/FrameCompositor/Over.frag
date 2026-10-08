#version 330 core
uniform sampler2D uImage;
in vec2 vUV;
out vec4 fragColor;
vec3 toLinear(vec3 c) { return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c)); }
vec3 toSrgb(vec3 c) { return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c)); }
void main() {
    // Supersampling averages an edge pixel's colour with the transparent background's black
    // in linear light before encoding, so the colour arrives multiplied by its coverage.
    // Undone here, so an edge blends over the viewer's background without a dark fringe.
    vec4 c = texture(uImage, vUV);
    if (c.a <= 0.0) discard;
    fragColor = vec4(clamp(toSrgb(toLinear(c.rgb) / c.a), 0.0, 1.0), c.a);
}