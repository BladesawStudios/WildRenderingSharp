#version 450 core
uniform sampler2D tex_depth;
uniform float uNear; uniform float uFar;
in vec2 vUV; out vec4 fragColor;
void main() {
    float d = texture(tex_depth, vUV).r;
    float ndc = d * 2.0 - 1.0;
    float viewZ = (2.0 * uNear * uFar) / (uFar + uNear - ndc * (uFar - uNear));
    fragColor = vec4(clamp((viewZ - uNear) / (uFar - uNear), 0.0, 1.0));
}
