// ---- WildRenderingSharp merged draw (MergedShaderPatch) ----
struct WrsDraw
{
    ivec4 instance;   // first vec4 of the batch's instances, the vec4s in one instance, the palette's vec4s, whether the palette repeats
    ivec4 shape;      // first vec4 of the shape's material, first handle of its textures, flags, spare
};
layout (binding = 1, std430) readonly buffer _WrsMaterials { vec4 wrs_materials[]; };
layout (binding = 3, std430) readonly buffer _WrsDraws { WrsDraw wrs_draws[]; };
layout (binding = 7, std430) readonly buffer _WrsInstances { vec4 wrs_inst[]; };
uniform int wrs_draw_base;
layout (location = 28) flat out ivec4 wrs_v;
WrsDraw wrs_d;
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
    if (i >= 0 && i < wrs_d.instance.z)
        return wrs_inst[wrs_base + 5 + i];
    if (wrs_d.instance.w != 0)
        return wrs_inst[wrs_base + 5 + row];
    return vec4(row == 0 ? 1.0 : 0.0, row == 1 ? 1.0 : 0.0, row == 2 ? 1.0 : 0.0, 0.0);
}

vec4 wrs_mat(int i)
{
    return wrs_materials[wrs_d.shape.x + i];
}
// ---- end merged draw ----
