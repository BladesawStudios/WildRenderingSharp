#version 450 core
layout (location = 0) in vec4 aPosition;
layout (location = 4) in vec4 aBlendWeight0;
layout (location = 5) in vec4 aBlendWeight1;
layout (location = 6) in vec4 aBlendIndex0;
layout (location = 7) in vec4 aBlendIndex1;

layout (binding = 2, std140) uniform _Mtx { vec4 data[4096]; } bones;

uniform mat4 uMVP;      // proj * view * model - SKIN_COUNT 0 only (positions are pre-posed)
uniform mat4 uViewProj; // proj * view - the palette already carries the model transform
uniform int uSkinCount;

const int kBoneSlots = 4096 / 3; // the _Mtx array holds 3 vec4 rows per bone

vec3 skinOne(vec3 p, float packedIndex)
{
    // Clamped because a slot is only as wide as the palette built; a stray index would read past the block, which is undefined.
    int slot = clamp(floatBitsToInt(packedIndex) & 0xFFFF, 0, kBoneSlots - 1);
    vec4 v = vec4(p, 1.0);
    return vec3(dot(v, bones.data[slot * 3 + 0]),
                dot(v, bones.data[slot * 3 + 1]),
                dot(v, bones.data[slot * 3 + 2]));
}

void main()
{
    vec3 p = aPosition.xyz;
    if (uSkinCount == 0)
    {
        gl_Position = uMVP * vec4(p, 1.0);
        return;
    }
    if (uSkinCount == 1)
    {
        // A single bind has no weight attribute in the real shader (the weight is an implicit 1.0), so the one slot is taken straight.
        gl_Position = uViewProj * vec4(skinOne(p, aBlendIndex0.x), 1.0);
        return;
    }
    vec3 skinned = skinOne(p, aBlendIndex0.x) * aBlendWeight0.x;
    skinned += skinOne(p, aBlendIndex0.y) * aBlendWeight0.y;
    if (uSkinCount >= 3) skinned += skinOne(p, aBlendIndex0.z) * aBlendWeight0.z;
    if (uSkinCount >= 4) skinned += skinOne(p, aBlendIndex0.w) * aBlendWeight0.w;
    if (uSkinCount >= 5) skinned += skinOne(p, aBlendIndex1.x) * aBlendWeight1.x;
    if (uSkinCount >= 6) skinned += skinOne(p, aBlendIndex1.y) * aBlendWeight1.y;
    if (uSkinCount >= 7) skinned += skinOne(p, aBlendIndex1.z) * aBlendWeight1.z;
    if (uSkinCount >= 8) skinned += skinOne(p, aBlendIndex1.w) * aBlendWeight1.w;
    gl_Position = uViewProj * vec4(skinned, 1.0);
}
