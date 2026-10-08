void main()
{
    wrs_base = (wrs_first_instance + gl_InstanceID) * wrs_instance_stride;
    wrs_inner_main();
}

