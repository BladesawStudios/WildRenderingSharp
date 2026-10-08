#version 330 core
in vec2 vUV;
uniform sampler2D tCloud;
out vec4 oCol;
void main() { oCol = texture(tCloud, vUV); }
