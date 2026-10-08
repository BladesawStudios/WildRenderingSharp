layout (binding = 27, std140) uniform _mrw_fade
{
    vec4 params;   // x = start distance, y = ramp, z = 1 for exponential, w = strength
    vec4 extents;  // xyz = dome half-extents in world units
    vec4 skyColor; // rgb = colour distant cloud fades toward
} mrw_fade;
