#version 330 core
in vec2 vUV;
uniform sampler2D tSrc;
out vec4 oCol;
void main() { oCol = texture(tSrc, vUV); }
