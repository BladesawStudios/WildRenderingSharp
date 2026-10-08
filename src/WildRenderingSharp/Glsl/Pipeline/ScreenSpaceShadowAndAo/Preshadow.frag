#version 450 core
uniform sampler2D tex_nld;
uniform sampler2DShadow tex_shadow;
uniform sampler2D tex_gnrm;     // cTex_GBuffNormal, same space as tex_nld
uniform mat4 uViewInv;          // Context decl 3 (cViewInv), promoted to 4x4
uniform mat4 uLightViewProj;
uniform vec2 uTanHalf;          // Context decl 7 = (tanHalfFovX, tanHalfFovY)
uniform vec3 uSunWorld;         // direction TOWARD the sun, world space
uniform float uNear, uFar, uBias, uTexel, uTexelWorld, uDepthRange;
// Cascades (FrameRequest.ShadowCascades): nested regions, finest first, one layer each.
uniform sampler2DArrayShadow tex_cascades;
uniform int uCascadeCount;
uniform mat4 uCascadeViewProj[4];
uniform vec4 uCascadeParams[4];   // x: texel size in metres, y: bias in light depth, z: PCF radius in texels
uniform float uCascadeTexel;
in vec2 vUV; out vec4 fragColor;

vec3 viewPos(vec2 uv) {
    float z = texture(tex_nld, uv).r * (uFar - uNear) + uNear;
    return vec3((uv.x * 2.0 - 1.0) * uTanHalf.x * z, (1.0 - uv.y * 2.0) * uTanHalf.y * z, -z);
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
    if (d >= 0.999) { fragColor = vec4(1.0, 0.0, 1.0, 1.0); return; }
    vec3 p = viewPos(vUV);
    vec4 world = uViewInv * vec4(p, 1.0);

    vec3 nView = decodeGBuffNormal(vUV);
    vec3 nWorld = normalize(mat3(uViewInv) * nView);

    float ndl = dot(nWorld, uSunWorld);

    if (uCascadeCount > 0) {
        vec3 nPerpC = nWorld - uSunWorld * ndl;
        float grazing = 2.8 * pow(1.0 - clamp(abs(ndl), 0.0, 1.0), 2.0);
        ivec2 ppC = ivec2(mod(gl_FragCoord.xy, 4.0));
        const float bayerC[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
        float angleC = bayerC[ppC.y * 4 + ppC.x] / 16.0 * 6.28318530718;
        float caC = cos(angleC), saC = sin(angleC);
        for (int c = 0; c < uCascadeCount; c++) {
            vec4 prm = uCascadeParams[c];
            vec3 wb = world.xyz + nWorld * prm.x + nPerpC * (prm.x * grazing);
            vec4 lcC = uCascadeViewProj[c] * vec4(wb, 1.0);
            vec3 scC = lcC.xyz / lcC.w * 0.5 + 0.5;
            // Inside this cascade with room for the filter, or try the next, coarser one.
            float margin = uCascadeTexel * (prm.z + 2.0);
            if (any(lessThan(scC.xy, vec2(margin))) || any(greaterThan(scC.xy, vec2(1.0 - margin))) || scC.z >= 1.0)
                continue;
            float refC = clamp(scC.z - prm.y, 0.0, 1.0);
            const vec2 discC[8] = vec2[8](
                vec2(-0.94201624, -0.39906216), vec2( 0.94558609, -0.76890725),
                vec2(-0.09418410, -0.92938870), vec2( 0.34495938,  0.29387760),
                vec2(-0.91588581,  0.45771432), vec2(-0.81544232, -0.87912464),
                vec2(-0.38277543,  0.27676845), vec2( 0.97484398,  0.75648379));
            float visC = 0.0;
            float rC = uCascadeTexel * prm.z;
            for (int i = 0; i < 8; i++) {
                vec2 s = vec2(discC[i].x * caC - discC[i].y * saC, discC[i].x * saC + discC[i].y * caC) * rC;
                visC += texture(tex_cascades, vec4(scC.xy + s, float(c), refC));
            }
            visC *= 0.125;
            fragColor = vec4(visC, 0.0, 1.0, visC);
            return;
        }
        // Past the last cascade: unshadowed, as the game is beyond its own.
        fragColor = vec4(1.0, 0.0, 1.0, 1.0);
        return;
    }

    // Normal-offset bias from preshading_chara prog 120:
    // baseOffset = 1.0 * uTexelWorld (prevents self-shadow at all angles)
    // grazingOffset = 2.8 * uTexelWorld * (1 - |ndl|)^2 (grazing-angle expansion)
    vec3 nPerp = nWorld - uSunWorld * ndl;
    float absNdl = clamp(abs(ndl), 0.0, 1.0);
    float grazingOffset = uTexelWorld * (2.8 * pow(1.0 - absNdl, 2.0));
    vec3 worldBiased = world.xyz + nWorld * uTexelWorld + nPerp * grazingOffset;

    vec4 lc = uLightViewProj * vec4(worldBiased, 1.0);
    vec3 sc = lc.xyz / lc.w * 0.5 + 0.5;
    if (any(lessThan(sc.xy, vec2(0.0))) || any(greaterThan(sc.xy, vec2(1.0)))) {
        fragColor = vec4(1.0, 0.0, 1.0, 1.0);
        return;
    }

    float bias = uBias / uDepthRange;
    float refZ = clamp(sc.z - bias, 0.0, 1.0);

    // TotK uses cascades with fine per-cascade texels; this uses a single wide-frustum ortho map. The game's 4-tap PCF at ±0.5 texel gives only 5 output values on a coarser map, and chara_skin's 8x ramp
    // turns those into hard staircases. So: 16-tap rotated Poisson disc PCF over ±3 shadow texels for a smooth 0 to 1 gradient the downstream Gaussian can work with, with the disc rotated per pixel via a
    // Bayer 4x4 matrix to break fixed-grid patterns.
    const vec2 disc[16] = vec2[16](
        vec2(-0.94201624, -0.39906216), vec2( 0.94558609, -0.76890725),
        vec2(-0.09418410, -0.92938870), vec2( 0.34495938,  0.29387760),
        vec2(-0.91588581,  0.45771432), vec2(-0.81544232, -0.87912464),
        vec2(-0.38277543,  0.27676845), vec2( 0.97484398,  0.75648379),
        vec2( 0.44323325, -0.97511554), vec2( 0.53742981, -0.47373420),
        vec2(-0.26496911, -0.41893023), vec2( 0.79197514,  0.19090188),
        vec2(-0.24188840,  0.99706507), vec2(-0.81409955,  0.91437590),
        vec2( 0.19984126,  0.78641367), vec2( 0.14383161, -0.14100790)
    );
    ivec2 pp = ivec2(mod(gl_FragCoord.xy, 4.0));
    const float bayer[16] = float[16](
         0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
        12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0,
         3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
        15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0
    );
    float angle = bayer[pp.y * 4 + pp.x] * 6.28318530718;
    float ca = cos(angle), sa = sin(angle);
    float r = uTexel * 3.0;  // 3-texel radius → smooth 0→1 penumbra gradient

    float vis = 0.0;
    for (int i = 0; i < 16; i++) {
        vec2 s = vec2(disc[i].x * ca - disc[i].y * sa,
                      disc[i].x * sa + disc[i].y * ca) * r;
        vis += texture(tex_shadow, vec3(sc.xy + s, refZ));
    }
    vis *= (1.0 / 16.0);

    fragColor = vec4(vis, 0.0, 1.0, vis);
}
