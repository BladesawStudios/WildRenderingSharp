#version 450 core
uniform sampler2D tex_src;
uniform vec2 uStep;
in vec2 vUV; out vec4 fragColor;
void main() {
    vec4 c0 = texture(tex_src, vUV);
    vec4 cL = texture(tex_src, vUV - uStep);
    vec4 cR = texture(tex_src, vUV + uStep);

    // Nintendo agl_preshading_filter (STEP1/STEP2): 3-tap bilinear Gaussian with weights [6, 5, 6] / 17
    fragColor = c0 * 0.294117659 + (cL + cR) * 0.3529412;
}
