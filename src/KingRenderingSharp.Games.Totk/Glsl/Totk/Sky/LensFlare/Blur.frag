#version 330 core
in vec2 vUV;
uniform sampler2D tSrc;
uniform vec2 uStep;
out vec4 oCol;
void main()
{
    vec3 c = texture(tSrc, vUV).rgb * 0.2270270270;
    c += texture(tSrc, vUV + uStep * 1.3846153846).rgb * 0.3162162162;
    c += texture(tSrc, vUV - uStep * 1.3846153846).rgb * 0.3162162162;
    c += texture(tSrc, vUV + uStep * 3.2307692308).rgb * 0.0702702703;
    c += texture(tSrc, vUV - uStep * 3.2307692308).rgb * 0.0702702703;
    oCol = vec4(c, 1.0);
}
