#version 330 core
in vec2 vUV;
uniform sampler2D tSrc;
uniform vec3 uGain;
uniform float uHorizonY;
out vec4 oCol;
void main()
{
    // The table's rows toward the horizon hold its longest light paths, where blue is extinguished completely and the colour
    // is one saturated line. The closer a row is to the horizon the more it takes the colour of the row at uHorizonY.
    vec4 c = texture(tSrc, vUV);
    vec4 anchor = texture(tSrc, vec2(vUV.x, uHorizonY));
    c = mix(c, anchor, smoothstep(uHorizonY, 0.5, vUV.y));
    oCol = vec4(c.rgb * uGain, c.a);
}
