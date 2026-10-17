#version 450 core
uniform sampler2D tex_src;
uniform sampler2D tex_depth;   // normalized linear depth at the same size as tex_src
uniform vec2 uStep;
in vec2 vUV; out vec4 fragColor;

// A neighbour counts only if it is on the same surface, by depth: blurred across an edge, the shadow of one surface and the unwritten values beside it
// make a pale rim of the other's.
const float SameSurface = 0.08;

void main() {
    ivec2 size = textureSize(tex_src, 0);
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    ivec2 step_texels = uStep.x > 0.0 ? ivec2(1, 0) : ivec2(0, 1);
    float d0 = texelFetch(tex_depth, pixel, 0).r;
    float dL = texelFetch(tex_depth, clamp(pixel - step_texels, ivec2(0), size - 1), 0).r;
    float dR = texelFetch(tex_depth, clamp(pixel + step_texels, ivec2(0), size - 1), 0).r;
    float wL = abs(dL - d0) <= SameSurface * d0 ? 0.3529412 : 0.0;
    float wR = abs(dR - d0) <= SameSurface * d0 ? 0.3529412 : 0.0;

    vec4 c0 = texture(tex_src, vUV);
    vec4 cL = texture(tex_src, vUV - uStep);
    vec4 cR = texture(tex_src, vUV + uStep);

    // Nintendo agl_preshading_filter (STEP1/STEP2): 3-tap bilinear Gaussian with weights [6, 5, 6] / 17, here kept to the pixel's own surface.
    float w0 = 0.294117659;
    fragColor = (c0 * w0 + cL * wL + cR * wR) / (w0 + wL + wR);
}
