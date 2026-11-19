#version 450 core
// Where the doorway cuts the rock away, where the hall is seen, and the depth the hall is clipped at. The doorway is the panel the fire burns around: from outside, its front, where no rock stands
// in front of it; with the eye inside the rock's hull (the nearest of its faces a back one), either side, whatever stands in front. The hall is seen through
// the doorway's front where nothing else stands in front of the panel, and with the eye inside the hull everywhere but the doorway, through which the world
// outside is seen.
uniform sampler2D uFront;       // window depth of the nearest front face of the hulls, drawn whole; 1 where none is
uniform sampler2D uBack;        // the same of the back faces
uniform sampler2D uPanel;       // window depth of the panel's front where it lands; 1 where it does not
uniform sampler2D uDoorway;     // red where the panel's front lands, green where either side does
uniform sampler2D uSceneDepth;  // the G-buffer's depth, with the rock cut
uniform int uMask;              // 0 the cut, before the rock is drawn; 1 where the hall is seen; 2 the depth the hall is clipped at, a window depth
uniform float uNear, uFar;
out vec4 fragColor;

// How far the panel may lie behind the rock's nearest face and still be seen: it spans the rock's arch, whose edge meets it.
const float ArchTolerance = 0.5;

// How far past the rock's surface the hall is clipped, in metres: the copy of the rock in the hall lies on it.
const float SurfaceDepth = 0.5;

float viewZ(float d) { return (uNear * uFar) / (uFar * (1.0 - d) + uNear * d); }
float windowDepth(float z) { return clamp(uFar * (z - uNear) / (z * (uFar - uNear)), 0.0, 1.0); }

void main() {
    ivec2 p = ivec2(gl_FragCoord.xy);
    float front = texelFetch(uFront, p, 0).r;
    float back = texelFetch(uBack, p, 0).r;
    float panel = texelFetch(uPanel, p, 0).r;
    vec2 doorway = texelFetch(uDoorway, p, 0).rg;
    bool insideRock = back < 1.0 && back < front;
    bool frontOpen = doorway.r > 0.5 && (front >= 1.0 || viewZ(panel) <= viewZ(front) + ArchTolerance);
    bool cut = frontOpen || (insideRock && doorway.g > 0.5);
    if (uMask == 0) {
        fragColor = vec4(cut ? 1.0 : 0.0);
        return;
    }
    // Seen through the doorway from outside, the hall starts just past the rock's front: whatever of it stands nearer is not seen. From inside the rock none of
    // it is clipped, as clipping it there left holes that showed the ground under the hall.
    if (uMask == 2) {
        float surface = !insideRock && frontOpen ? front : 0.0;
        fragColor = vec4(surface > 0.0 ? windowDepth(viewZ(surface) + SurfaceDepth) : 0.0);
        return;
    }
    float scene = texelFetch(uSceneDepth, p, 0).r;
    bool throughDoorway = frontOpen && panel <= scene + 1e-6;
    fragColor = vec4(throughDoorway || (insideRock && !cut) ? 1.0 : 0.0);
}
