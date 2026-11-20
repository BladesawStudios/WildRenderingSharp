#version 450 core
layout (binding = 28) uniform sampler2D krs_gbuffer_depth;
layout (binding = 3, std140) uniform _KrsStamp { vec4 krs_stamp; vec4 krs_viewport; };
layout (location = 0) out vec4 fragColor;
float viewDepth(float d)
{
    float n = krs_stamp.y, f = krs_stamp.z;
    return (2.0 * n * f) / (f + n - (d * 2.0 - 1.0) * (f - n));
}
void main()
{
    vec2 g = vec2(gl_FragCoord.x * krs_viewport.x, 1.0 - gl_FragCoord.y * krs_viewport.y);
    float kept = texture(krs_gbuffer_depth, g).r;
    if (kept >= 1.0)
        discard;
    float zKept = viewDepth(kept), zThis = viewDepth(gl_FragCoord.z);
    if (abs(zKept - zThis) > max(0.02, zKept * 0.002))
        discard;
    fragColor = vec4(krs_stamp.x, 0.0, 0.0, 1.0);
}
