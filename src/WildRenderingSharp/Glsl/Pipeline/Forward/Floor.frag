#version 450 core
uniform sampler2D t;       // Scene: deferred + forward, additively combined
uniform sampler2D tFloor;  // Behind: deferred-only, captured before any forward drawing
uniform int uFlip;
in vec2 vUV;
out vec4 fragColor;
void main() {
    vec2 uv = uFlip == 1 ? vec2(vUV.x, 1.0 - vUV.y) : vUV;
    vec3 combined = texture(t, uv).rgb;
    vec3 floorRgb = texture(tFloor, uv).rgb;
    fragColor = vec4(max(combined, floorRgb), texture(t, uv).a);
}
