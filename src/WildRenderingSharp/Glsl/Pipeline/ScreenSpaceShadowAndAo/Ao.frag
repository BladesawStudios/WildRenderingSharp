#version 450 core
uniform sampler2D tex_nld;
uniform sampler2D tex_gnrm;
uniform vec2 uTanHalf; uniform vec2 uPix;
uniform float uNear, uFar, uRadius, uStrength;
uniform vec3 uSunView;
uniform int uDebugNrm;
in vec2 vUV; out vec4 fragColor;

vec3 viewPos(vec2 uv) {
    float d = texture(tex_nld, uv).r;
    float z = d * (uFar - uNear) + uNear;
    return vec3((uv.x * 2.0 - 1.0) * uTanHalf.x * z, (1.0 - uv.y * 2.0) * uTanHalf.y * z, -z);
}

float ditherAngle(vec2 fc) {
    ivec2 p = ivec2(mod(fc, 4.0));
    const float dither4x4[16] = float[16](
         0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
        12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
         3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
        15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
    );
    return dither4x4[p.y * 4 + p.x] * 6.28318530718;
}

vec3 decodeGBuffNormal(vec2 uv) {
    vec4 g = texture(tex_gnrm, uv);
    int zb = int(trunc(g.z * 255.0));
    if ((zb & 8) == 0) return vec3(0.0, 0.0, 1.0);
    float sx = ((zb & 2) != 0) ? 1.0 : -1.0;
    float sy = ((zb & 1) != 0) ? 1.0 : -1.0;
    float u2 = g.x * g.x + g.y * g.y;
    float zHalf = sqrt(max(0.0, 1.0 - u2));
    vec3 n = vec3(g.x * zHalf * sx * 2.0, g.y * zHalf * sy * 2.0, 1.0 - 2.0 * u2);
    float len2 = dot(n, n);
    return (len2 > 1e-6) ? n * inversesqrt(len2) : vec3(0.0, 0.0, 1.0);
}

void main() {
    float d = texture(tex_nld, vUV).r;
    if (d >= 0.999) { fragColor = vec4(1.0); return; }
    vec3 p = viewPos(vUV);
    float z = -p.z;
    vec3 n = decodeGBuffNormal(vUV);
    vec2 projRadius = (uRadius / max(0.001, z)) * vec2(1.0 / (2.0 * uTanHalf.x), 1.0 / (2.0 * uTanHalf.y));
    vec2 screenRadius = clamp(projRadius, uPix * 1.5, vec2(0.05));
    float rot = ditherAngle(gl_FragCoord.xy);
    float occ = 0.0;
    const int NUM_PAIRS = 5;
    const int N = NUM_PAIRS * 2;
    float r2 = uRadius * uRadius;
    float bias = 0.12 * uRadius;
    for (int i = 0; i < NUM_PAIRS; ++i) {
        float alpha = (float(i) + 0.5) / float(NUM_PAIRS);
        float angle = alpha * 3.14159265 + rot;
        float r = (alpha * 0.75 + 0.25);
        vec2 dir = vec2(cos(angle), sin(angle)) * r * screenRadius;
        vec2 uv1 = vUV + dir;
        if (uv1.x >= 0.0 && uv1.x <= 1.0 && uv1.y >= 0.0 && uv1.y <= 1.0) {
            float qd1 = texture(tex_nld, uv1).r;
            if (qd1 < 0.999) {
                vec3 q1 = viewPos(uv1);
                vec3 v1 = q1 - p;
                float d2_1 = dot(v1, v1);
                if (d2_1 > 1e-7 && d2_1 < r2) {
                    float vn1 = dot(v1, n) - bias;
                    if (vn1 > 0.0) {
                        float f1 = max(0.0, 1.0 - d2_1 / r2);
                        occ += (f1 * f1 * f1) * (vn1 / (d2_1 + 0.0001));
                    }
                }
            }
        }
        vec2 uv2 = vUV - dir;
        if (uv2.x >= 0.0 && uv2.x <= 1.0 && uv2.y >= 0.0 && uv2.y <= 1.0) {
            float qd2 = texture(tex_nld, uv2).r;
            if (qd2 < 0.999) {
                vec3 q2 = viewPos(uv2);
                vec3 v2 = q2 - p;
                float d2_2 = dot(v2, v2);
                if (d2_2 > 1e-7 && d2_2 < r2) {
                    float vn2 = dot(v2, n) - bias;
                    if (vn2 > 0.0) {
                        float f2 = max(0.0, 1.0 - d2_2 / r2);
                        occ += (f2 * f2 * f2) * (vn2 / (d2_2 + 0.0001));
                    }
                }
            }
        }
    }
    float ao = clamp(1.0 - (uStrength * 0.6) * occ * (2.0 / float(N)) * uRadius, 0.25, 1.0);
    if (uDebugNrm == 1) { fragColor = vec4(n * 0.5 + 0.5, 1.0); return; }
    fragColor = vec4(1.0, 1.0, 1.0, ao);
}
