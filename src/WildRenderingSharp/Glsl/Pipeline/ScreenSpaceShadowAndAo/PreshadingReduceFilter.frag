#version 450 core
uniform sampler2D tex_src;
uniform vec2 uStep;
in vec2 vUV; out vec4 fragColor;
void main() {
    vec4 c0 = texture(tex_src, vUV - uStep * 0.5);
    vec4 c1 = texture(tex_src, vUV + uStep * 0.5);
    // Nintendo agl_preshading_filter (STEP4/STEP5): 2-tap average
    fragColor = (c0 + c1) * 0.5;
}
