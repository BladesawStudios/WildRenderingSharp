

// ---- WildRenderingSharp: explicit distance fade (not the game's own - see CloudDistanceFade) ----
layout (location = 8) in vec4 mrw_local;

void main()
{
    mrw_inner_main();

    // HORIZONTAL radius only, deliberately. The dome mesh is
    // (radius*sin, height, radius*cos) with the apex at (0,1,0), so the xz length runs
    // 0 straight overhead to 1 at the horizon - which is exactly "how distant is this
    // cloud" on a dome centred on the eye. Including the height term (the first version
    // of this) made overhead clouds read as FAR once the dome's height was large, so
    // the fade hit the nearest clouds hardest and the far ones outlasted them.
    float dist = length(mrw_local.xz) * mrw_fade.extents.x;
    float over = max(0.0, dist - mrw_fade.params.x);

    // Normalised by the start distance so the ramp means the same thing whatever scale
    // the dome happens to be; without that, "ramp" would silently change meaning with
    // SkyScale.
    float t = over * mrw_fade.params.y / max(1.0, mrw_fade.params.x);
    float fade = (mrw_fade.params.z > 0.5) ? exp(-t) : clamp(1.0 - t, 0.0, 1.0);
    fade = mix(1.0, fade, clamp(mrw_fade.params.w, 0.0, 1.0));

    // The dome stretches its noise across the last few degrees above the horizon until it aliases; the game hides that band
    // behind terrain, which a model viewer does not have.
    fade *= smoothstep(0.05, 0.3, mrw_local.y);

    // Distant cloud loses opacity AND takes the sky's colour, which is what actually
    // reads as distance - fading alpha alone just makes far cloud thin, not far away.
    out_attr0.rgb = mix(mrw_fade.skyColor.rgb, out_attr0.rgb, fade);
    out_attr0.a *= fade;
}
