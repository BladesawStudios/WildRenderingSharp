#version 450 core
uniform sampler2D tex_alb;   // cTex_GBuffAlbedo  (G-buffer attachment 1) - Scene is already in this texture's own flipped space, no extra flip needed
uniform sampler2D tex_emis;  // cTex_GBuffEmission (G-buffer attachment 5)
uniform vec2 uViewportSize;
uniform float uEmission;
uniform float uEmissionExposureRcp;
layout (location = 0) out vec4 fragColor;

void main()
{
    vec2 uv = gl_FragCoord.xy / uViewportSize;
    vec3 albedo = texture(tex_alb, uv).rgb;

    // Green is the visibility mask: a continuous multiplier (0 = eye not visible, 1 = fully visible), so a partially green pixel gets proportionally dimmed emission.
    // Red plays a separate role (it suppresses a distinct over-brightness artifact) and must not be folded in.
    float mask = albedo.g;
    vec3 emission = max(texture(tex_emis, uv).rgb, vec3(0.0)) * uEmission * uEmissionExposureRcp;
    fragColor = vec4(emission * mask, 1.0);
}
