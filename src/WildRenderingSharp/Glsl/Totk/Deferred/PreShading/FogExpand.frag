#version 450 core
uniform sampler2D tex_fog;        // the pre-shading's fog, at half resolution
uniform sampler2D tex_depth;      // normalized linear depth, full resolution
uniform sampler2D tex_half_depth; // the same at half resolution
out vec4 fragColor;

// One fog colour per pixel from the half-resolution texel whose depth is nearest the pixel's own, in place of a blend of the four around it: a blend
// across a silhouette puts the fog of one surface on the edge of the other.
void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    ivec2 half_size = textureSize(tex_fog, 0);
    float depth = texelFetch(tex_depth, pixel, 0).r;

    // The four half-resolution texels whose centres surround this pixel's centre.
    ivec2 first = ivec2(floor(vec2(pixel) * 0.5 - 0.25));
    float best = 1e30;
    ivec2 chosen = clamp(pixel / 2, ivec2(0), half_size - 1);
    for (int dy = 0; dy < 2; dy++)
    {
        for (int dx = 0; dx < 2; dx++)
        {
            ivec2 texel = clamp(first + ivec2(dx, dy), ivec2(0), half_size - 1);
            float difference = abs(texelFetch(tex_half_depth, texel, 0).r - depth);
            if (difference < best)
            {
                best = difference;
                chosen = texel;
            }
        }
    }
    fragColor = texelFetch(tex_fog, chosen, 0);
}
