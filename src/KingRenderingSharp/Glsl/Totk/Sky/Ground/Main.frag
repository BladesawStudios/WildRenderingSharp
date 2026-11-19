#version 330 core
in vec2 vUV;
uniform mat3 uViewInv;
uniform vec2 uTanHalf;
uniform vec3 uGround;
out vec4 oCol;

void main()
{
    vec2 ndc = vUV * 2.0 - 1.0;
    vec3 dir = normalize(uViewInv * vec3(ndc.x * uTanHalf.x, ndc.y * uTanHalf.y, -1.0));
    // Blended over the sky from just below the horizon, so the haze there carries into the ground.
    oCol = vec4(uGround, smoothstep(0.03, -0.04, dir.y));
}
