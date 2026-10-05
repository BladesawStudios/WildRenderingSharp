using System.Numerics;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// WildRenderingSharp's OWN distance fade for the cloud dome, injected into the real <c>agl_cloud</c> shader
/// as a small GLSL patch.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is deliberately not the game's fade.</b> The real shader has one - alpha subtracts
/// <c>clamp((farness - FarAlphaChgStart) / FarAlphaChgEnd)</c>, driven through
/// <c>in_attr1.w = temp_100 * Common[5].w</c> - and driving it correctly from WildRenderingSharp was tried
/// repeatedly and never produced a visible falloff. Rather than keep guessing at a blob slot, this
/// adds an explicit, user-controlled fade on top and says so plainly. The game's own path is left
/// exactly as it is; if it ever starts working, this can be turned off with one slider.
/// </para>
/// <para>
/// <b>How the patch works.</b> The decompiled shaders are renamed <c>main</c> -&gt;
/// <c>mrw_inner_main</c> and given a new <c>main</c> that calls it and then adjusts the outputs.
/// That is why this needs no understanding of the shader body at all, and cannot disturb it: every
/// original instruction still runs, unmodified, in the original order. The vertex half only adds a
/// varying carrying the dome's own LOCAL vertex position, which is the one thing the fragment stage
/// has no other way to know.
/// </para>
/// <para>
/// The local position is a UNIT dome, so its HORIZONTAL radius (xz) runs 0 straight overhead to 1
/// at the horizon - scaled by the dome's real extent, that is the view distance, because the dome
/// is always centred on the eye. No matrices required, which keeps this independent of the
/// Y-up/Z-up and projection questions the rest of the pass has to care about. The height component
/// is deliberately excluded; including it made overhead clouds read as the far ones.
/// </para>
/// </remarks>
public static class CloudDistanceFade
{
    /// <summary>Binding for the patch's own uniform block - clear of every block the real shader declares.</summary>
    public const uint Binding = 27;

    const string BlockDecl = """
        layout (binding = 27, std140) uniform _mrw_fade
        {
            vec4 params;   // x = start distance, y = ramp, z = 1 for exponential, w = strength
            vec4 extents;  // xyz = dome half-extents in world units
            vec4 skyColor; // rgb = colour distant cloud fades toward
        } mrw_fade;
        """;

    /// <summary>Adds a varying carrying the unit dome's local vertex position.</summary>
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

    /// <summary>Applies the fade to the finished fragment colour and alpha.</summary>
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

    /// <summary>The 48-byte block the patch reads.</summary>
    public static byte[] BuildUbo(float startDistance, float ramp, bool exponential, float strength,
        Vector3 extents, Vector3 skyColor)
    {
        var buf = new byte[48];
        void F(int slot, int comp, float v) => BitConverter.GetBytes(v).CopyTo(buf, slot * 16 + comp * 4);
        F(0, 0, MathF.Max(0f, startDistance));
        F(0, 1, MathF.Max(0f, ramp));
        F(0, 2, exponential ? 1f : 0f);
        F(0, 3, strength);
        F(1, 0, extents.X); F(1, 1, extents.Y); F(1, 2, extents.Z);
        F(2, 0, skyColor.X); F(2, 1, skyColor.Y); F(2, 2, skyColor.Z);
        return buf;
    }
}
