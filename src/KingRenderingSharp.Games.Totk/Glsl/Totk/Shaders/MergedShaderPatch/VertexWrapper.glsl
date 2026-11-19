void main()
{
    krs_d = krs_draws[krs_draw_base + gl_DrawIDARB];
    krs_base = krs_d.instance.x + (gl_BaseInstanceARB + gl_InstanceID) * krs_d.instance.y;
    // The baked-lighting atlas this instance has, as the two halves of its bindless handle, where the shape takes it from the instance.
    vec4 atlas = (krs_d.shape.z & 1) != 0 ? krs_inst[krs_base + 4] : vec4(0.0);
    krs_v = ivec4(krs_d.shape.x, krs_d.shape.y, floatBitsToInt(atlas.x), floatBitsToInt(atlas.y));
    krs_inner_main();
}
