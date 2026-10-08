#version 450 core
uniform sampler2D t; uniform vec3 uBalance; uniform float uThreshold, uClamp;
in vec2 vUV; out vec4 fragColor;
void main() {
    vec3 c = texture(t, vUV).rgb * uBalance;
    c = max(c - uThreshold, 0.0);
    fragColor = vec4(min(c, uClamp), 1.0);
}
