#version 450 core
// Writes the depth inside or outside a mask, and leaves the other side alone: the near plane, so nothing drawn next lands there; the far plane, so whatever is
// drawn next wins there; the depth kept from before, to put it back; or another window depth, where it is under one.
uniform sampler2D uMask;
uniform sampler2D uSavedDepth;
uniform sampler2D uFrom;
uniform int uInside;   // 1 writes where the mask is set, 0 where it is not
uniform int uValue;    // 0 the near plane, 1 the far plane, 2 the kept depth, 3 uFrom's

void main() {
    ivec2 p = ivec2(gl_FragCoord.xy);
    bool masked = texelFetch(uMask, p, 0).r > 0.5;
    if (masked != (uInside == 1))
        discard;
    if (uValue == 3) {
        float from = texelFetch(uFrom, p, 0).r;
        if (from >= 1.0)
            discard;
        gl_FragDepth = from;
        return;
    }
    gl_FragDepth = uValue == 0 ? 0.0 : uValue == 1 ? 1.0 : texelFetch(uSavedDepth, p, 0).r;
}
