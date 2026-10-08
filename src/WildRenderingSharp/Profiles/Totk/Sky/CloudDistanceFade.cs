using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>
/// WildRenderingSharp's OWN distance fade for the cloud dome, injected into the real <c>agl_cloud</c> shader as a small GLSL patch.
/// </summary>
public static class CloudDistanceFade
{
    public const uint Binding = 27;

    const string BlockDecl = """
        layout (binding = 27, std140) uniform _mrw_fade
        {
            vec4 params;   // x = start distance, y = ramp, z = 1 for exponential, w = strength
            vec4 extents;  // xyz = dome half-extents in world units
            vec4 skyColor; // rgb = colour distant cloud fades toward
        } mrw_fade;
        """;

    public static string PatchVertex(string source)
    {
        if (source.Contains("mrw_inner_main"))
            return source;
        source = source.Replace("void main()", "void mrw_inner_main()");
        return source + """


            // ---- WildRenderingSharp: hand the fragment stage the dome's own local position ----
            layout (location = 8) out vec4 mrw_local;

            void main()
            {
                mrw_inner_main();
                mrw_local = vec4(in_attr0.xyz, 1.0);
            }
            """;
    }

    public static string PatchFragment(string source)
    {
        if (source.Contains("mrw_inner_main"))
            return source;
        source = source.Replace("void main()", "void mrw_inner_main()");
        return source + "\n\n" + BlockDecl + """


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

                // Distant cloud loses opacity AND takes the sky's colour, which is what actually
                // reads as distance - fading alpha alone just makes far cloud thin, not far away.
                out_attr0.rgb = mix(mrw_fade.skyColor.rgb, out_attr0.rgb, fade);
                out_attr0.a *= fade;
            }
            """;
    }

    public static byte[] BuildUbo(float startDistance, float ramp, bool exponential, float strength,
        Vector3 extents, Vector3 skyColor)
    {
        var buf = new byte[48];
        var u = new UniformWriter(buf);
        u.Set(0, 0, MathF.Max(0f, startDistance));
        u.Set(0, 1, MathF.Max(0f, ramp));
        u.Set(0, 2, exponential ? 1f : 0f);
        u.Set(0, 3, strength);
        u.Set(1, 0, extents.X); u.Set(1, 1, extents.Y); u.Set(1, 2, extents.Z);
        u.Set(2, 0, skyColor.X); u.Set(2, 1, skyColor.Y); u.Set(2, 2, skyColor.Z);
        return buf;
    }
}
