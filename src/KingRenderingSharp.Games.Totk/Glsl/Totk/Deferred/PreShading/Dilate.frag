#version 450 core
uniform sampler2D tex_src;        // a copy of the buffer being dilated
uniform sampler2D tex_half_depth; // normalized linear depth at the buffer's size
out vec4 fragColor;

// Where nothing was drawn, the sky, takes the mean of the drawn texels around it. The resolves read these buffers with a plain bilinear fetch, and at the
// edge of a surface that reaches a texel of whatever the approximation left there, lit and unoccluded, as a pale rim.
const float Sky = 0.9999;

bool drawn(ivec2 texel)
{
    return texelFetch(tex_half_depth, texel, 0).r < Sky;
}

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    ivec2 size = textureSize(tex_src, 0);
    if (drawn(pixel))
        discard;

    vec4 sum = vec4(0.0);
    float count = 0.0;
    for (int dy = -1; dy <= 1; dy++)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            ivec2 other = clamp(pixel + ivec2(dx, dy), ivec2(0), size - 1);
            if (drawn(other))
            {
                sum += texelFetch(tex_src, other, 0);
                count += 1.0;
            }
        }
    }
    if (count == 0.0)
        discard;
    fragColor = sum / count;
}
