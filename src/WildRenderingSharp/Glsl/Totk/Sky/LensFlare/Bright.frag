#version 330 core
in vec2 vUV;
uniform sampler2D tSrc;
uniform float uThreshold;
uniform float uExposure;
uniform vec2 uSrcTexel;
uniform sampler2D tDepth;
uniform int uSkyOnly;
out vec4 oCol;
vec3 bright(vec2 uv)
{
    // Sky-only source: the game's flare is a sun effect (gated on effect_sun_occlusion_cs), so geometry never throws one, whereas an emissive
    // weapon past any threshold did here. GBufferDepth is in the G-buffer's flipped orientation, hence 1 - v; far-plane depth is sky.
    if (uSkyOnly != 0 && texture(tDepth, vec2(uv.x, 1.0 - uv.y)).r < 0.99999)
        return vec3(0.0);
    // Thresholded in display space (after exposure) with the excess compressed below 1 on the brightest channel, hue kept: unbounded excess let one emissive object bury the frame in ghosts.
    vec3 e = max(texture(tSrc, uv).rgb * uExposure - uThreshold, 0.0);
    float m = max(e.r, max(e.g, e.b));
    return m > 0.0 ? e * (1.0 / (1.0 + m)) : vec3(0.0);
}
void main()
{
    // Four bilinear taps one source texel off-centre cover the whole 4x4 footprint of a quarter-res texel, so a thin bright edge cannot slip between samples and shimmer.
    oCol = vec4(0.25 * (bright(vUV + uSrcTexel * vec2(-1.0, -1.0))
                      + bright(vUV + uSrcTexel * vec2( 1.0, -1.0))
                      + bright(vUV + uSrcTexel * vec2(-1.0,  1.0))
                      + bright(vUV + uSrcTexel * vec2( 1.0,  1.0))), 1.0);
}
