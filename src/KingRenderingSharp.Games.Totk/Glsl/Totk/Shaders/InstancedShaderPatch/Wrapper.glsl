void main()
{
    krs_base = (krs_first_instance + gl_InstanceID) * krs_instance_stride;
    krs_inner_main();
}

