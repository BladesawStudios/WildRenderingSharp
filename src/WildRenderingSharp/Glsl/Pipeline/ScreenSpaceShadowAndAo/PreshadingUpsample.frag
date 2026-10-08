#version 450 core
uniform sampler2D tex_src;
in vec2 vUV; out vec4 fragColor;
void main() {
    fragColor = texture(tex_src, vUV);
}
