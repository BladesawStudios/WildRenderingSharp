// ---- KingRenderingSharp merged draw (MergedShaderPatch) ----
struct KrsDraw
{
    ivec4 instance;   // first vec4 of the batch's instances, the vec4s in one instance, the palette's vec4s, whether the palette repeats
    ivec4 shape;      // first vec4 of the shape's material, first handle of its textures, flags, spare
};
layout (binding = 1, std430) readonly buffer _KrsMaterials { vec4 krs_materials[]; };
layout (binding = 3, std430) readonly buffer _KrsDraws { KrsDraw krs_draws[]; };
layout (binding = 7, std430) readonly buffer _KrsInstances { vec4 krs_inst[]; };
uniform int krs_draw_base;
layout (location = 28) flat out ivec4 krs_v;
KrsDraw krs_d;
int krs_base;

vec4 krs_shp(int i)
{
    if (i >= 0 && i < 3) return krs_inst[krs_base + i];
    if (i == 8) return krs_inst[krs_base + 3];
    return vec4(0.0);
}

vec4 krs_mtx(int i)
{
    int row = ((i % 3) + 3) % 3;
    if (i >= 0 && i < krs_d.instance.z)
        return krs_inst[krs_base + 5 + i];
    if (krs_d.instance.w != 0)
        return krs_inst[krs_base + 5 + row];
    return vec4(row == 0 ? 1.0 : 0.0, row == 1 ? 1.0 : 0.0, row == 2 ? 1.0 : 0.0, 0.0);
}

vec4 krs_mat(int i)
{
    return krs_materials[krs_d.shape.x + i];
}
// ---- end merged draw ----
