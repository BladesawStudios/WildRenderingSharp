#version 450 core
uniform sampler2D t; uniform float k; in vec2 vUV; out vec4 fragColor;
void main() { fragColor = vec4(texture(t, vUV).rgb * k, 1.0); }
