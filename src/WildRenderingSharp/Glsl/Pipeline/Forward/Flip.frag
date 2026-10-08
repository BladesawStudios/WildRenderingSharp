#version 450 core
uniform sampler2D t;
uniform int uFlip;
in vec2 vUV;
out vec4 fragColor;
void main() {
    vec4 c = texture(t, uFlip == 1 ? vec2(vUV.x, 1.0 - vUV.y) : vUV);
    // Insurance against an already-broken upstream value on the plain pre-forward copies; the real combine step is FloorFragmentSource.
    fragColor = vec4(max(c.rgb, -1.0), c.a);
}
