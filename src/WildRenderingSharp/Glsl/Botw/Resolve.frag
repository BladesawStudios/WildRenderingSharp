#version 450 core
uniform sampler2D tAlbedo;
uniform sampler2D tNormal;
uniform sampler2D tDepth;
uniform vec3 uSunView;
uniform vec3 uSunColor;
uniform vec3 uHemiSky;
uniform vec3 uHemiGround;
uniform vec3 uBackground;
uniform int uView;
uniform int uFlip;
in vec2 vUV;
out vec4 fragColor;

void main() {
    vec2 uv = uFlip == 1 ? vec2(vUV.x, 1.0 - vUV.y) : vUV;
    if (texture(tDepth, uv).r >= 1.0) {
        fragColor = vec4(uBackground, 1.0);
        return;
    }
    vec3 albedo = texture(tAlbedo, uv).rgb;
    vec3 n = normalize(texture(tNormal, uv).xyz * 2.0 - 1.0);
    if (uView == 1) { fragColor = vec4(texture(tAlbedo, uv).rgb, 1.0); return; }
    if (uView == 2) { fragColor = vec4(n * 0.5 + 0.5, 1.0); return; }
    float sun = max(dot(n, uSunView), 0.0);
    vec3 ambient = mix(uHemiGround, uHemiSky, n.y * 0.5 + 0.5);
    fragColor = vec4(albedo * (ambient + uSunColor * sun), 1.0);
}
