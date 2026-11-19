#version 450 core
uniform sampler2D t;
in vec2 vUV;
out float o;
void main()
{
    float d = texture(t, vUV).r;
    o = d >= 0.9999 ? 1.0e6 : d;
}
