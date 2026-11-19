#version 450 core
uniform float uId;
uniform sampler2D tex_gbuf_depth;
uniform vec2 uInvViewport;
uniform vec2 uNearFar;
layout (location = 0) out vec4 fragColor;
float viewDepth(float d) {
    float n = uNearFar.x, f = uNearFar.y;
    return (2.0 * n * f) / (f + n - (d * 2.0 - 1.0) * (f - n));
}
void main() {
    vec2 g = vec2(gl_FragCoord.x * uInvViewport.x, 1.0 - gl_FragCoord.y * uInvViewport.y);
    float kept = texture(tex_gbuf_depth, g).r;
    if (kept >= 1.0)
        discard;
    float zKept = viewDepth(kept), zThis = viewDepth(gl_FragCoord.z);
    if (abs(zKept - zThis) > max(0.02, zKept * 0.002))
        discard;
    fragColor = vec4(uId, 0.0, 0.0, 1.0);
}
