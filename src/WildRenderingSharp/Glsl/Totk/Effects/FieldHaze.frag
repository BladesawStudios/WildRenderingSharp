#version 330 core
uniform sampler2D tDepth;
uniform float uNear;
uniform float uFar;
uniform vec2 uViewScale;
uniform vec3 uColor;
uniform int uCount;
uniform float uStrength;
uniform float uFalloff;
uniform float uSkyInfluence;
// Each level's sphere centre ahead of the camera, its radius, and its extinction per metre.
uniform vec3 uLevels[8];
// The normalized depth at and beyond which a pixel is sky.
const float SkyDepth = 0.9999;

in vec2 vUV;
out vec4 fragColor;

// How much of the view ray, from the camera to the given distance, lies inside the sphere.
float lengthInSphere(vec3 direction, float distance, float offset, float radius)
{
    // The centre is straight ahead of the camera, and the ray's direction is a unit vector.
    float along = -direction.z * offset;
    float discriminant = along * along - (offset * offset - radius * radius);
    if (discriminant < 0.0)
        return 0.0;
    float half_chord = sqrt(discriminant);
    return max(0.0, min(along + half_chord, distance) - max(along - half_chord, 0.0));
}

void main()
{
    // The view ray through this pixel, one unit in front of the camera.
    vec3 ray = vec3((vUV * 2.0 - 1.0) * uViewScale, -1.0);
    float scale = length(ray);
    // The depth is stored from the top row down, as the game draws it, and the image from the bottom up.
    float depth = texture(tDepth, vec2(vUV.x, 1.0 - vUV.y)).r;

    // The game never veils the sky, which has its own haze, so the veil over it is only as much as the influence says.
    bool sky = depth >= SkyDepth;
    if (sky && uSkyInfluence <= 0.0)
        discard;

    float distance = sky ? 1e6 : (uNear + depth * (uFar - uNear)) * scale;

    float opticalDepth = 0.0;
    for (int level = 0; level < uCount; level++)
        opticalDepth += uLevels[level].z * lengthInSphere(ray / scale, distance, uLevels[level].x, uLevels[level].y);

    // The cover grows with the optical depth to a power below one, so the near haze is thick enough and the far haze does not close in on one.
    fragColor = vec4(uColor, (1.0 - exp(-pow(opticalDepth, uFalloff) * uStrength)) * (sky ? uSkyInfluence : 1.0));
}
