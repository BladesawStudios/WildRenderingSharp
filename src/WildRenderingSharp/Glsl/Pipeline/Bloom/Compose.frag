#version 450 core
uniform sampler2D l0; uniform sampler2D l1; uniform sampler2D l2; uniform sampler2D l3;
uniform vec4 c0; uniform vec4 c1; uniform vec4 c2; uniform vec4 c3;
uniform vec3 uCompose; uniform float uIntensity;
in vec2 vUV; out vec4 fragColor;
void main() {
    vec3 b = texture(l0, vUV).rgb * c0.rgb * c0.a
           + texture(l1, vUV).rgb * c1.rgb * c1.a
           + texture(l2, vUV).rgb * c2.rgb * c2.a
           + texture(l3, vUV).rgb * c3.rgb * c3.a;
    fragColor = vec4(b * uCompose * uIntensity, 1.0);
}
