// ---- KingRenderingSharp merged draw (MergedShaderPatch) ----
layout (binding = 1, std430) readonly buffer _KrsMaterials { vec4 krs_materials[]; };
layout (binding = 2, std430) readonly buffer _KrsHandles { uvec2 krs_handles[]; };
layout (location = 28) flat in ivec4 krs_v;

vec4 krs_fmat(int i)
{
    return krs_materials[krs_v.x + i];
}
// ---- end merged draw ----
