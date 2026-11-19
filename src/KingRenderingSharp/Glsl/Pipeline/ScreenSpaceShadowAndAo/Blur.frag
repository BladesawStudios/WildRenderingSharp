#version 450 core
uniform sampler2D tex_ao; uniform sampler2D tex_nld;
uniform vec2 uStep;
uniform float uNear, uFar, uRadius;
in vec2 vUV; out vec4 fragColor;
void main() {
    float dc = texture(tex_nld, vUV).r;
    if (dc >= 0.999) { fragColor = vec4(1.0); return; }
    vec4 centerSample = texture(tex_ao, vUV);
    float zc = dc * (uFar - uNear) + uNear;
    float sigma_z = max(0.02 * zc, uRadius * 0.5);
    float inv2SigmaZ2 = 0.5 / (sigma_z * sigma_z);
    vec4 acc = vec4(0.0);
    float wsum = 0.0;
    for (int i = -6; i <= 6; ++i) {
        vec2 uv = vUV + uStep * float(i);
        float d = texture(tex_nld, uv).r;
        if (d >= 0.999) continue;
        float z = d * (uFar - uNear) + uNear;
        float dz = z - zc;
        float w = exp(-float(i * i) / 18.0) * exp(-dz * dz * inv2SigmaZ2);
        acc += texture(tex_ao, uv) * w;
        wsum += w;
    }
    fragColor = (wsum > 1e-5) ? (acc / wsum) : centerSample;
}
