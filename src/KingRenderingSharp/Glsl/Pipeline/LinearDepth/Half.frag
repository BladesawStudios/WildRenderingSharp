#version 450 core
uniform sampler2D tex_nld;
in vec2 vUV; out vec4 fragColor;

// The normalized depth at and beyond which a pixel is sky.
const float Sky = 0.9999;

// The farthest surface of the four, sky aside. A texel that holds a near surface and a far one is then the far one's, so the sliver of far surface beside a
// near silhouette still finds a texel at its own depth in the resolves' depth-matched reads, and shows its own lighting and fog; the near surface has texels of
// its own to read from. With the nearest, the sliver took the near surface's, and showed it as a bright line. The sky is left out because a ridge a pixel or two
// wide against it would otherwise find every texel around it taken for sky, and match none, and show a dark line.
void main() {
    vec4 s = textureGather(tex_nld, vUV, 0);
    float surface = -1.0;
    for (int i = 0; i < 4; i++)
        if (s[i] < Sky)
            surface = max(surface, s[i]);
    fragColor = vec4(surface < 0.0 ? max(max(s.x, s.y), max(s.z, s.w)) : surface);
}
