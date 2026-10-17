void main()
{
    wrs_d = wrs_draws[wrs_draw_base + gl_DrawIDARB];
    wrs_base = wrs_d.instance.x + (gl_BaseInstanceARB + gl_InstanceID) * wrs_d.instance.y;
    // The baked-lighting atlas this instance has, as the two halves of its bindless handle, where the shape takes it from the instance.
    vec4 atlas = (wrs_d.shape.z & 1) != 0 ? wrs_inst[wrs_base + 4] : vec4(0.0);
    wrs_v = ivec4(wrs_d.shape.x, wrs_d.shape.y, floatBitsToInt(atlas.x), floatBitsToInt(atlas.y));
    wrs_inner_main();
}
