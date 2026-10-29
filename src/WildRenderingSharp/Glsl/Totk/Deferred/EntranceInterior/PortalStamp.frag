#version 450 core
// Stamps the entrance's pass over every pixel seen through a shrine's doorway. The pass-ID mask is stored the other way up from the G-buffer.
uniform sampler2D uPortal;
uniform float uPassId;
out vec4 fragColor;

void main() {
    ivec2 size = textureSize(uPortal, 0);
    ivec2 p = ivec2(gl_FragCoord.x, size.y - 1 - int(gl_FragCoord.y));
    if (texelFetch(uPortal, p, 0).r < 0.5)
        discard;
    fragColor = vec4(uPassId, 0.0, 0.0, 1.0);
}
