#version 450 core
uniform sampler2D t;
uniform float uScale;
in vec2 vUV;
out vec4 fragColor;
void main() {
    vec3 c = texture(t, vec2(vUV.x, 1.0 - vUV.y)).rgb;
    if (any(isnan(c)) || any(isinf(c))) c = vec3(0.0);
    fragColor = vec4(max(c, vec3(0.0)) * uScale, 1.0);
}
