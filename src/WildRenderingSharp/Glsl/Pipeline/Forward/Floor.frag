#version 450 core
uniform sampler2D t;       // Scene: deferred + forward, additively combined
uniform sampler2D tFloor;  // Behind: deferred-only, captured before any forward drawing
uniform int uFlip;
uniform float uScale;      // From the units the forward programs draw in back to the frame's
in vec2 vUV;
out vec4 fragColor;
void main() {
    vec2 uv = uFlip == 1 ? vec2(vUV.x, 1.0 - vUV.y) : vUV;
    vec4 c = max(texture(t, uv), texture(tFloor, uv));
    // The alpha is the host's coverage, which a blended shape's own alpha would otherwise overwrite.
    fragColor = vec4(c.rgb * uScale, c.a);
}
