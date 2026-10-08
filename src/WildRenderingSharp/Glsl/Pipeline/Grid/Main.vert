#version 450 core
layout (location = 0) in vec2 aPos; // [-1,1] quad corner, scaled/positioned in the shader
uniform mat4 uViewProj;
uniform float uExtent;
out vec2 vWorldXY;
void main()
{
    vWorldXY = aPos * uExtent;
    gl_Position = uViewProj * vec4(vWorldXY, 0.0, 1.0);
}
