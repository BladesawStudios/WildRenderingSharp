#version 450 core
// cTex_LocalReflection as the game's entrance depth program writes it in its cut mode: the metres from where the view enters a shrine entrance's hull to the
// surface it sees, less one, over 199, which the entrance pre-shading pass reads back as d * 199 + 1 to ramp its haze over.
uniform sampler2D uDoorDepth;   // window depth of the nearest front face of the hulls; 1 where none is
uniform sampler2D uSceneDepth;  // the G-buffer's depth
uniform float uNear, uFar;
out vec4 fragColor;

// As LinearDepth's, written without the subtraction of two near-equal numbers.
float viewZ(float d) { return (uNear * uFar) / (uFar * (1.0 - d) + uNear * d); }

void main() {
    ivec2 p = ivec2(gl_FragCoord.xy);
    float door = texelFetch(uDoorDepth, p, 0).r;
    float scene = viewZ(texelFetch(uSceneDepth, p, 0).r);
    // No front face in front of the surface means the eye is inside the hull, and the room starts at the eye.
    float behind = scene - (door >= 1.0 ? 0.0 : viewZ(door));
    fragColor = vec4(clamp((behind - 1.0) / 199.0, 0.0, 1.0));
}
