// ---- WildRenderingSharp instancing (InstancedShaderPatch) ----
layout (binding = 7, std430) readonly buffer _WrsInstances { vec4 wrs_inst[]; };
uniform int wrs_first_instance;
uniform int wrs_instance_stride;
uniform int wrs_palette_vec4s;
uniform int wrs_palette_repeat;
int wrs_base;

vec4 wrs_shp(int i)
{
    if (i >= 0 && i < 3) return wrs_inst[wrs_base + i];
    if (i == 8) return wrs_inst[wrs_base + 3];
    return vec4(0.0);
}

vec4 wrs_mtx(int i)
{
    int row = ((i % 3) + 3) % 3;
    if (i >= 0 && i < wrs_palette_vec4s)
        return wrs_inst[wrs_base + 4 + i];
    if (wrs_palette_repeat != 0)
        return wrs_inst[wrs_base + 4 + row];
    return vec4(row == 0 ? 1.0 : 0.0, row == 1 ? 1.0 : 0.0, row == 2 ? 1.0 : 0.0, 0.0);
}
// ---- end instancing ----


