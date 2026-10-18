// ---- WildRenderingSharp merged draw (MergedShaderPatch) ----
layout (binding = 1, std430) readonly buffer _WrsMaterials { vec4 wrs_materials[]; };
layout (binding = 2, std430) readonly buffer _WrsHandles { uvec2 wrs_handles[]; };
layout (location = 28) flat in ivec4 wrs_v;

vec4 wrs_fmat(int i)
{
    return wrs_materials[wrs_v.x + i];
}
// ---- end merged draw ----
