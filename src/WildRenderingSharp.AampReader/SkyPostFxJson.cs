using System.Text.Json;
using Nintendo.Aamp;

namespace WildRenderingSharp.AampReader;

/// <summary>
/// The ONLY public entry point of this assembly - deliberately <c>byte[]</c>-in/<c>string</c>-out
/// (JSON), never a <c>Nintendo.Aamp.*</c> or <c>Syroot.*</c> type, and never a <c>WildRenderingSharp</c>
/// type either. See <see cref="WildRenderingSharp.AampReader"/>'s project file for why this assembly is loaded
/// into an isolated <c>AssemblyLoadContext</c> rather than referenced directly - crossing that
/// boundary with anything but plain BCL types (here: <c>byte[]</c>/<c>string</c>) would need the
/// SAME type loaded into both contexts, which defeats the whole point of the isolation.
/// </summary>
public static class SkyPostFxJson
{
    /// <param name="skyBytes">Raw bytes of <c>postfx/master_field.baglsky</c>, or null to skip.</param>
    /// <param name="cloudBytes">Raw bytes of <c>postfx/master_field.baglclwd</c>, or null to skip.</param>
    /// <returns>A JSON object with optional <c>"sky"</c>/<c>"cloud"</c> keys - see <see cref="ParseSky"/>/<see cref="ParseCloud"/> for their shape.</returns>
    public static string ParseToJson(byte[]? skyBytes, byte[]? cloudBytes, byte[]? colorCorrectionBytes = null)
    {
        var result = new Dictionary<string, object?>();
        if (skyBytes is not null) result["sky"] = ParseSky(skyBytes);
        if (cloudBytes is not null) result["cloud"] = ParseCloud(cloudBytes);
        if (colorCorrectionBytes is not null) result["colorCorrection"] = ParseColorCorrection(colorCorrectionBytes);
        return JsonSerializer.Serialize(result);
    }

    /// <summary>
    /// The real <c>agl::pfx::ColorCorrection</c> object from <c>postfx/*.baglccr</c>.
    /// </summary>
    /// <remarks>
    /// Generic, like <see cref="ParseCloud"/>, because this object is mostly simple scalars and
    /// colours and naming each one buys nothing. The <c>level</c> curve array is deliberately not
    /// parsed - AAMP curves are a different value type and evaluating them needs a curve
    /// interpolator, so they are left out rather than half-read.
    /// </remarks>
    static Dictionary<string, object?> ParseColorCorrection(byte[] bytes)
    {
        var file = AampFile.FromBinary(bytes);
        var obj = file.RootNode.Objects("color_correction");
        return obj is null ? new Dictionary<string, object?>() : ParseObject(obj);
    }

    /// <summary>The real <c>agl::pfx::Sky</c> "sky" object's fields WildRenderingSharp's <c>BackgroundPass</c> actually consumes - see that class and <c>WildRenderingSharp.Rendering.SkyPostFx</c> for what each one drives.</summary>
    static Dictionary<string, object?> ParseSky(byte[] bytes)
    {
        var d = new Dictionary<string, object?>();
        var file = AampFile.FromBinary(bytes);
        var sky = file.RootNode.Objects("sky");
        if (sky is null)
            return d;

        void F(string name)
        {
            if (sky.Params(name)?.Value is float f) d[name] = f;
        }
        void C(string name)
        {
            if (sky.Params(name)?.Value is Color4F c) d[name] = new[] { c.R, c.G, c.B, c.A };
        }

        foreach (var name in new[]
        {
            "rayleigh_base_height", "mie_base_height", "mie_scattering_coeff", "mie_symmetrical_prop_rendering",
            "rayleigh_amplifier_rendering", "mie_amplifier_rendering", "render_sun_intensity", "render_sun_size",
            "render_sun_lerp", "scatter_fog_near", "scatter_fog_far", "scatter_fog_density", "scatter_fog_atten", "scatter_fog_horz",
            // adhoc fog - the horizon haze band. sky_postfx_sky's USE_ADHOC_FOG=1 variant reads
            // exactly three scalars plus a colour, and these are them; the _grd/_near/_far members
            // belong to the GROUND pass (distance fog over real geometry), not the sky.
            "adhoc_fog_near", "adhoc_fog_far", "adhoc_fog_atten_grd", "adhoc_fog_atten_sky",
            "adhoc_fog_atten_minscale_sky",
        })
            F(name);

        // Unnamed in AampLibrary's own hash table (resolved by raw CRC32 hash instead - stable
        // regardless of name-table coverage) - the per-channel Rayleigh scattering coefficient,
        // sitting right after static_rayleigh_base_height in every dump of this file.
        if (sky.Params(3995358905u)?.Value is Syroot.Maths.Vector3F v)
            d["rayleigh_scattering_coeff"] = new[] { v.X, v.Y, v.Z };

        C("sun_color");
        C("ground_color");
        C("adhoc_fog_color"); // .a is the fog DENSITY, not a blend alpha - see SkyPostFxPass
        return d;
    }

    /// <summary>
    /// The real <c>agl::fx::Cloud</c> config: the top-level "Cloud" object (shared, non-layered
    /// settings) plus every field of <c>CloudParam0</c>/<c>CloudParam1</c> (the two real blended
    /// cloud layers - <c>CloudParam2</c> is a duplicate of <c>CloudParam1</c> in the shipped file,
    /// not a third distinct layer, so it's not read here).
    ///
    /// Unlike <see cref="ParseSky"/>'s old hand-picked subset, this dumps EVERY resolvable field by
    /// name via <see cref="ParseObject"/> rather than naming each one - the real object has ~55
    /// fields per layer (see <c>WildRenderingSharp.Rendering.CloudPostFxLayer</c> for what each drives) and
    /// hand-listing them invites exactly the kind of silent gap the old 4-field subset had. A field
    /// whose name AampLibrary can't resolve to a string (a handful of undocumented ones - confirmed
    /// against a real dump: 0xdd74d2cb, 0x809fe782, 0xebaab4f0, 0x74ac7329, 0xabac209b, 0x405f2706)
    /// is skipped rather than keyed by raw hash, since nothing on the WildRenderingSharp side would know what to
    /// do with it anyway.
    /// </summary>
    static Dictionary<string, object?> ParseCloud(byte[] bytes)
    {
        var d = new Dictionary<string, object?>();
        var file = AampFile.FromBinary(bytes);

        var cloud = file.RootNode.Objects("Cloud");
        if (cloud is not null) d["cloud"] = ParseObject(cloud);

        var layer0 = file.RootNode.Objects("CloudParam0");
        if (layer0 is not null) d["layer0"] = ParseObject(layer0);

        var layer1 = file.RootNode.Objects("CloudParam1");
        if (layer1 is not null) d["layer1"] = ParseObject(layer1);

        return d;
    }

    static readonly System.Text.RegularExpressions.Regex UnresolvedHash =
        new(@"^0x[0-9a-fA-F]{8}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Every resolvable param of an AAMP object, keyed by its real name exactly as authored (e.g. <c>"mAlphaThreshold"</c>) - see <see cref="ParseCloud"/> for why this is generic rather than a hand-picked field list.</summary>
    static Dictionary<string, object?> ParseObject(ParamObject obj)
    {
        var d = new Dictionary<string, object?>();
        foreach (var entry in obj.ParamEntries)
        {
            string name = entry.HashString;
            if (UnresolvedHash.IsMatch(name))
                continue;

            d[name] = entry.Value switch
            {
                float f => f,
                bool b => b,
                int i => i,
                uint u => u,
                Color4F c => new[] { c.R, c.G, c.B, c.A },
                _ => null,
            };
        }
        return d;
    }
}
