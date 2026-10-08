#version 450 core
uniform sampler2D t; uniform vec2 uStep; in vec2 vUV; out vec4 fragColor;
void main() {
    float w[5] = float[](0.2270270, 0.1945946, 0.1216216, 0.0540541, 0.0162162);
    vec3 acc = texture(t, vUV).rgb * w[0];
    for (int i = 1; i < 5; ++i) {
        acc += texture(t, vUV + uStep * float(i)).rgb * w[i];
        acc += texture(t, vUV - uStep * float(i)).rgb * w[i];
    }
    fragColor = vec4(acc, 1.0);
}
