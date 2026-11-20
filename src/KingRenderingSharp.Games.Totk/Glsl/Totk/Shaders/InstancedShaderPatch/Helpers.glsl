// ---- KingRenderingSharp instancing (InstancedShaderPatch) ----
layout (binding = 7, std430) readonly buffer _KrsInstances { vec4 krs_inst[]; };
uniform int krs_first_instance;
uniform int krs_instance_stride;
uniform int krs_palette_vec4s;
uniform int krs_palette_repeat;
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
    if (i >= 0 && i < krs_palette_vec4s)
        return krs_inst[krs_base + 4 + i];
    if (krs_palette_repeat != 0)
        return krs_inst[krs_base + 4 + row];
    return vec4(row == 0 ? 1.0 : 0.0, row == 1 ? 1.0 : 0.0, row == 2 ? 1.0 : 0.0, 0.0);
}
// ---- end instancing ----


