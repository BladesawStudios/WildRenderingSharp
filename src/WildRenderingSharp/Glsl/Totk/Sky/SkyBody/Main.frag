#version 330 core
in vec2 vUV;
uniform sampler2D tSun;      // BC4 disc mask, R only
uniform sampler2D tMoon;     // BC5 sprite: R = albedo, G = coverage
uniform mat3 uViewInv;       // camera-to-world rotation, rows already Y-up swapped
uniform vec2 uTanHalf;
uniform vec3 uSunDir;        // world, Y-up, normalised
uniform vec3 uMoonDir;
uniform vec3 uSunColor;
uniform vec3 uMoonColor;
uniform float uSunRadius;    // angular radius, radians
uniform float uMoonRadius;
uniform int uDrawSun, uDrawMoon;
out vec4 oCol;

// Places `dir` into a tangent frame around `centre` and returns sprite UVs in [0,1],
// plus whether the pixel is inside the sprite at all.
bool bodyUv(vec3 dir, vec3 centre, float radius, out vec2 uv)
{
    // Reject behind the body or beyond its disc; the dot test keeps a body from also appearing at its antipode, which a tangent-plane projection would allow.
    if (dot(dir, centre) <= 0.0) return false;
    vec3 up = abs(centre.y) > 0.99 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
    vec3 right = normalize(cross(up, centre));
    vec3 realUp = cross(centre, right);
    // Divide by the dot so the sprite stays square toward the screen edge: a gnomonic projection, as a real billboard does.
    float d = dot(dir, centre);
    vec2 t = vec2(dot(dir, right), dot(dir, realUp)) / (d * radius);
    uv = t * 0.5 + 0.5;
    return all(greaterThanEqual(uv, vec2(0.0))) && all(lessThanEqual(uv, vec2(1.0)));
}

void main()
{
    vec2 ndc = vUV * 2.0 - 1.0;
    vec3 viewRay = vec3(ndc.x * uTanHalf.x, ndc.y * uTanHalf.y, -1.0);
    vec3 dir = normalize(uViewInv * viewRay);

    vec3 acc = vec3(0.0);
    float cover = 0.0;
    vec2 uv;

    // Moon first so the sun composites over it on the rare overlap.
    if (uDrawMoon == 1 && bodyUv(dir, uMoonDir, uMoonRadius, uv))
    {
        vec2 m = texture(tMoon, vec2(uv.x, 1.0 - uv.y)).rg;
        // R is the lit albedo, G the coverage or phase mask; multiplying keeps the unlit limb transparent instead of a dark disc.
        float a = m.r * m.g;
        acc += uMoonColor * a;
        cover = max(cover, a);
    }

    if (uDrawSun == 1 && bodyUv(dir, uSunDir, uSunRadius, uv))
    {
        float s = texture(tSun, vec2(uv.x, 1.0 - uv.y)).r;
        acc += uSunColor * s;
        cover = max(cover, s);
    }

    oCol = vec4(acc, cover);
}
