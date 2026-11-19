#version 450 core
uniform sampler2D tex_depth; // normalized linear depth at the buffer's size
uniform float uNear;
uniform float uFar;
uniform vec2 uFade;          // the view depths where a shadow starts to fade to lit and where it is gone
out vec4 fragColor;

// The share of lit to blend in, in every channel: the blend takes each channel a share of the way to one.
void main()
{
    float z = texelFetch(tex_depth, ivec2(gl_FragCoord.xy), 0).r * (uFar - uNear) + uNear;
    float fade = clamp((z - uFade.x) / (uFade.y - uFade.x), 0.0, 1.0);
    fragColor = vec4(fade);
}
