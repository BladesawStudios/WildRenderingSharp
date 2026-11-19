#version 450 core
uniform sampler2D t;
uniform sampler2D tEmission;
in vec2 vUV;
out vec4 fragColor;

void main() {
    vec2 uv = vec2(vUV.x, 1.0 - vUV.y);
    fragColor = vec4(texture(t, uv).rgb + texture(tEmission, uv).rgb, 1.0);
}
