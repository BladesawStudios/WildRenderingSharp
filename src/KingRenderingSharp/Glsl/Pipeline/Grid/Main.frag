#version 450 core
in vec2 vWorldXZ;
uniform vec3 uCameraPos;
uniform vec4 uLineColor;
uniform float uExtent;
uniform float uMinorCell;
layout (location = 0) out vec4 fragColor;

// One anti-aliased line set at the given world cell size, using screen-space derivatives so line thickness stays about 1px at any distance (no geometry, no texture).
// `vis` is the anti-aliasing guard and is not optional. `deriv` is in cells per pixel, so once it reaches 1 a whole cell fits in one pixel and the line test stops meaning anything: the
// numerator is bounded by 0.5 while the denominator keeps growing, so `line` saturates to 1 for every fragment and the grid becomes a solid sheet. That happened with 1/10-unit cells on a
// 13,880-unit quad (Enemy_Dragon_Darkness, radius 694) whose bounds centre is below the ground, so the sheet covered the upper screen and read as the sky's colour inverted. Fading a level
// out as its cells approach pixel size makes that impossible.
float gridLines(vec2 p, float cell, out float vis)
{
    vec2 coord = p / cell;
    vec2 deriv = fwidth(coord);
    vis = clamp(1.0 - max(deriv.x, deriv.y), 0.0, 1.0);
    vec2 grid = abs(fract(coord - 0.5) - 0.5) / max(deriv, 1e-6);
    return 1.0 - clamp(min(grid.x, grid.y), 0.0, 1.0);
}

void main()
{
    float dist = distance(vWorldXZ, uCameraPos.xz);
    float fade = clamp(1.0 - dist / uExtent, 0.0, 1.0);
    fade *= fade;

    // The cell size comes from the caller, scaled to the scene (see Run).
    float minorVis, majorVis;
    float minor = gridLines(vWorldXZ, uMinorCell, minorVis) * minorVis;
    float major = gridLines(vWorldXZ, uMinorCell * 10.0, majorVis) * majorVis;
    float line = max(minor * 0.35, major * 0.8);

    if (line * fade < 0.01) discard;
    fragColor = vec4(uLineColor.rgb, uLineColor.a * line * fade);
}
