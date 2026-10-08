#version 450 core
uniform sampler2D tex_nld;
uniform sampler2D tex_gnrm;
uniform mat4 uViewInv;
uniform vec2 uTanHalf;
uniform float uNear, uFar;
uniform vec3 uSunWorld;   // direction TOWARD the sun, world space
uniform vec3 uSunColor, uHemiSky, uHemiGround;
uniform float uDirect;    // 1 with the sun in it, 0 for the ambient alone
in vec2 vUV; out vec4 fragColor;

vec3 viewPos(vec2 uv) {
    float z = texture(tex_nld, uv).r * (uFar - uNear) + uNear;
    return vec3((uv.x * 2.0 - 1.0) * uTanHalf.x * z, (1.0 - uv.y * 2.0) * uTanHalf.y * z, -z);
}

vec3 decodeGBuffNormal(vec2 uv) {
    vec4 g = texture(tex_gnrm, uv);
    int zb = int(trunc(g.z * 255.0));
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
    if (d >= 0.999) { fragColor = vec4(uHemiSky, 1.0); return; }
    vec3 nView = decodeGBuffNormal(vUV);
    vec3 nWorld = normalize(mat3(uViewInv) * nView);
    vec3 ambient = mix(uHemiGround, uHemiSky, nWorld.z * 0.5 + 0.5); // the renderer's world is Z-up
    vec3 direct = uSunColor * max(0.0, dot(nWorld, uSunWorld)) * uDirect;
    fragColor = vec4(ambient + direct, 1.0);
}
