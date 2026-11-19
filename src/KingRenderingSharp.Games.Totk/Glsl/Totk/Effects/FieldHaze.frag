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
// Each level's fade in and fade out bounds, in view depth, and its soft edge distance.
uniform vec4 uBands[8];
uniform float uSoft[8];
// How many steps the ray is measured in through each sphere.
const int Steps = 40;
// The normalized depth at and beyond which a pixel is sky.
const float SkyDepth = 0.9999;

in vec2 vUV;
out vec4 fragColor;

// The fades of a level's sprites by their view depth: in over the first two bounds and, where there are two more, out over those.
float bandWeight(vec4 band, float depth)
{
    float weight = band.y > band.x ? clamp((depth - band.x) / (band.y - band.x), 0.0, 1.0) : 1.0;
    if (band.w > band.z)
        weight *= 1.0 - clamp((depth - band.z) / (band.w - band.z), 0.0, 1.0);
    return weight;
}

// How much of the view ray, from the camera to the given distance, lies inside the sphere, each point of it counted by how much of a sprite shows there.
float coverInSphere(int level, vec3 direction, float distance, float sceneDepth)
{
    float offset = uLevels[level].x, radius = uLevels[level].y;
    // The centre is straight ahead of the camera, and the ray's direction is a unit vector.
    float cosine = -direction.z;
    float along = cosine * offset;
    float discriminant = along * along - (offset * offset - radius * radius);
    if (discriminant < 0.0)
        return 0.0;
    float half_chord = sqrt(discriminant);
    float near = max(along - half_chord, 0.0), far = min(along + half_chord, distance);
    if (far <= near)
        return 0.0;

    float step = (far - near) / float(Steps);
    float soft = uSoft[level];
    float sum = 0.0;
    for (int i = 0; i < Steps; i++)
    {
        float depth = (near + (float(i) + 0.5) * step) * cosine;
        float weight = bandWeight(uBands[level], depth);
        // A sprite shows less the nearer it is in front of the surface.
        if (soft > 0.0)
            weight *= clamp((sceneDepth - depth) / soft, 0.0, 1.0);
        sum += weight;
    }
    return sum * step;
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
        opticalDepth += uLevels[level].z * coverInSphere(level, ray / scale, distance, sky ? 1e6 : uNear + depth * (uFar - uNear));

    // The cover grows with the optical depth to a power below one, so the near haze is thick enough and the far haze does not close in on one.
    fragColor = vec4(uColor, (1.0 - exp(-pow(opticalDepth, uFalloff) * uStrength)) * (sky ? uSkyInfluence : 1.0));
}
