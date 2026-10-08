#version 450 core
uniform sampler2D tex_nld;
in vec2 vUV; out vec4 fragColor;
void main() {
    vec4 s = textureGather(tex_nld, vUV, 0);
    fragColor = vec4(min(min(s.x, s.y), min(s.z, s.w)));
}
