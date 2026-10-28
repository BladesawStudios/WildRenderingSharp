#version 450 core
uniform sampler2D tex_depth;
uniform float uNear; uniform float uFar;
in vec2 vUV; out vec4 fragColor;
void main() {
    float d = texture(tex_depth, vUV).r;
    // The view depth of window depth d, written without the subtraction of two near-equal numbers the usual form ends in: with a far plane ten thousand
    // times the near one, that subtraction put the sky, at exactly 1, at 98 per cent of the range and so took it for terrain.
    float viewZ = (uNear * uFar) / (uFar * (1.0 - d) + uNear * d);
    fragColor = vec4(clamp((viewZ - uNear) / (uFar - uNear), 0.0, 1.0));
}
