#version 450 core
out vec2 vUV;
void main() {
    float x = -1.0 + float((gl_VertexID & 1) * 4);
    float y = -1.0 + float((gl_VertexID & 2) * 2);
    vUV = vec2(x, y) * 0.5 + 0.5;
    gl_Position = vec4(x, y, 0.0, 1.0);
}
