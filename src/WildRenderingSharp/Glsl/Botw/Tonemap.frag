#version 450 core
uniform sampler2D t;
uniform float uExposure;
in vec2 vUV;
out vec4 fragColor;

void main() {
    vec3 c = texture(t, vUV).rgb * uExposure;
    fragColor = vec4(c / (1.0 + c), 1.0);
}
