#version 450 core
uniform sampler2D t;
uniform int uFlip;
uniform float uScale;
in vec2 vUV;
out vec4 fragColor;
void main() {
    vec4 c = texture(t, uFlip == 1 ? vec2(vUV.x, 1.0 - vUV.y) : vUV);
    fragColor = vec4(c.rgb * uScale, c.a);
}
