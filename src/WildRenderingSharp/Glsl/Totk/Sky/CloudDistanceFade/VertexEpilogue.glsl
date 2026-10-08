

// ---- WildRenderingSharp: hand the fragment stage the dome's own local position ----
layout (location = 8) out vec4 mrw_local;

void main()
{
    mrw_inner_main();
    mrw_local = vec4(in_attr0.xyz, 1.0);
}
