#version 450 core
uniform sampler2D t; uniform float uKnee, uCeil;
in vec2 vUV; out vec4 fragColor;
void main() {
    vec3 c = texture(t, vUV).rgb;
    float m = max(max(c.r, c.g), c.b);
    if (m > uKnee) {
        float excess = m - uKnee;
        float range = max(uCeil - uKnee, 1e-4);
        float compressed = uKnee + excess / (1.0 + excess / range);
        c *= compressed / max(m, 1e-6);
    }
    fragColor = vec4(c, 1.0);
}
