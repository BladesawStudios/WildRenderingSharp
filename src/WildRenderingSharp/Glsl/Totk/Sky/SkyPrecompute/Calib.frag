#version 330 core
in vec2 vUV;
uniform sampler2D tSrc;
uniform vec3 uGain;
out vec4 oCol;
void main() { vec4 c = texture(tSrc, vUV); oCol = vec4(c.rgb * uGain, c.a); }
