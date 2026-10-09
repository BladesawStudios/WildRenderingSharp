#version 450 core
uniform sampler2D t;
in vec2 vUV;
out vec4 fragColor;

void main() {
    fragColor = vec4(texture(t, vec2(vUV.x, 1.0 - vUV.y)).rgb, 1.0);
}
