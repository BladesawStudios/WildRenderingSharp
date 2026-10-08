using System.Numerics;
using System.Text.Json;
using BymlLibrary;

namespace WildRenderingSharp.Rendering;

/// <summary>
/// A <c>game::wm::ResEnvPalette</c> record: the per-time-of-day and weather parameter set the runtime feeds the
/// renderer, loaded from romfs (<c>Pack/EnvPalette.pack.zs</c>; see <see cref="EnvPaletteLibrary.LoadFromRomfs"/>).
/// </summary>
/// <remarks>
/// The source data is loosely typed: some palettes omit fields others have and inherit them through <c>$parent</c>
/// (resolved before this class sees them), and colours arrive as a <c>{R,G,B,A}</c> map. So this wraps a plain
/// key/value bag with named accessors for the fields the renderer reads. The <c>IconCapture</c> and <c>UI</c> presets
/// exist only in code and use <c>[r,g,b,a]</c> arrays; <see cref="TryGetColor"/> accepts both shapes.
/// </remarks>
public sealed class EnvPalette
{
    readonly IReadOnlyDictionary<string, object?> _raw;

    public string Name { get; }

    public EnvPalette(string name, IReadOnlyDictionary<string, object?> raw)
    {
        Name = name;
        _raw = raw;
    }

    /// <summary>Every field name this palette carries after <c>$parent</c> inheritance, for auditing which authored values the renderer consumes.</summary>
    public IReadOnlyCollection<string> Keys => _raw.Keys.ToArray();

    public bool TryGetFloat(string key, out float value)
    {
        if (_raw.TryGetValue(key, out var raw))
        {
            switch (raw)
            {
                case double d: value = (float)d; return true;
                case string s when float.TryParse(s, out var f): value = f; return true;
            }
        }
        value = default;
        return false;
    }

    public float GetFloat(string key, float fallback) => TryGetFloat(key, out var v) ? v : fallback;

    public bool GetBool(string key, bool fallback) => _raw.TryGetValue(key, out var raw) && raw is bool b ? b : fallback;

    public bool TryGetColor(string key, out Vector4 value)
    {
        if (_raw.TryGetValue(key, out var raw))
        {
            // The code-defined presets use [r,g,b,a] arrays; romfs palettes use a {R,G,B,A} map (see FromByml).
            if (raw is object?[] { Length: >= 3 } arr)
            {
                float w = arr.Length >= 4 ? ToFloat(arr[3]) : 1f;
                value = new Vector4(ToFloat(arr[0]), ToFloat(arr[1]), ToFloat(arr[2]), w);
                return true;
            }
            if (raw is IReadOnlyDictionary<string, object?> map && map.ContainsKey("R"))
            {
                map.TryGetValue("A", out var a);
                value = new Vector4(ToFloat(map["R"]), ToFloat(map.GetValueOrDefault("G")), ToFloat(map.GetValueOrDefault("B")), a is null ? 1f : ToFloat(a));
                return true;
            }
        }
        value = default;
        return false;
    }

    public Vector4 GetColor(string key, Vector4 fallback) => TryGetColor(key, out var v) ? v : fallback;

    static float ToFloat(object? v) => v switch
    {
        double d => (float)d,
        string s when float.TryParse(s, out var f) => f,
        _ => 0f,
    };

    // Named accessors for the fields the renderer reads.

    static Vector3 Xyz(Vector4 v) => new(v.X, v.Y, v.Z);

    public Vector3 BgDifColor => Xyz(GetColor("BgDifColor", new Vector4(1, 1, 1, 0)));
    public float BgDifIntensity => GetFloat("BgDifIntensity", 5.0f);

    public bool HasHemiColors => _raw.ContainsKey("HemiSkyColor") && _raw.ContainsKey("HemiGroundColor");
    public Vector3 HemiSkyColor => Xyz(GetColor("HemiSkyColor", Vector4.Zero));
    public Vector3 HemiGroundColor => Xyz(GetColor("HemiGroundColor", Vector4.Zero));
    public float HemiIntensity => GetFloat("HemiIntensity", 1.0f);

    /// <summary>A direct multiplier on the hemisphere ambient, authored by 44 of the 131 shipped palettes (absent = 1.0). Multiplies the ambient slider rather than replacing it.</summary>
    public float AmbientScale => GetFloat("AmbientScale", 1f);

    // Sky scattering: what the game feeds its own sky shader, and what the ambient approximation uses (see AmbientLighting).

    /// <summary>How much blue Rayleigh scattering this sky has: 1.0 at overworld noon, 0.25 at night, 0.0 under a blood moon.</summary>
    public float SkyRayleighAmplifier => GetFloat("SkyRParam_rayleigh_amplifier", 1f);
    /// <summary>How much sun-coloured Mie haze this sky has: 12 at overworld noon, 0 at night, 256 under a blood moon.</summary>
    public float SkyMieAmplifier => GetFloat("SkyRParam_mie_amplifier", 0f);
    /// <summary>The Mie phase asymmetry. The ambient approximation uses only the amplifiers and the sun colour.</summary>
    public float SkyMieSymmetrical => GetFloat("SkyRParam_mie_symmetrical", 0f);
    /// <summary>The sun colour as the sky sees it, tinting the Mie haze. Distinct from <see cref="BgDifColor"/>, the directional light that hits surfaces.</summary>
    public Vector3 SkySunColor => Xyz(GetColor("SkySunColor", new Vector4(1, 1, 1, 1)));
    public float SkySunColorIntensity => GetFloat("SkySunColorIntensity", 1f);
    /// <summary>The palette's own "ignore my SkySunColor" switch.</summary>
    public bool SkySunColorNoUse => GetBool("SkySunColorNoUse", false);

    /// <summary>The palette's distance-fog colour, also used by <c>BackgroundPass</c> as the horizon haze tint.</summary>
    public Vector3 FogColor => Xyz(GetColor("FogColor", new Vector4(0.6f, 0.75f, 1f, 0f)));

    /// <summary>The palette's fog far distance. A captured cloud-shader constant buffer holds <c>1/FogEnd</c> (1/300 for that capture's palette); see <c>CloudDomePass</c>.</summary>
    public float FogEnd => GetFloat("FogEnd", 300f);
    public float FogStart => GetFloat("FogStart", 0f);

    /// <summary><c>FogColor</c>'s alpha: the adhoc-fog density and the palette's on/off for the horizon haze band. Noon authors 0; BloodyMoon_DarknessDragon authors 0.6 with a pure-red <c>FogColor</c>.</summary>
    public float FogDensity => GetColor("FogColor", new Vector4(0.6f, 0.75f, 1f, 0f)).W;

    /// <summary>"Af" is adhoc fog: per-palette counterparts of <c>master_field.baglsky</c>'s <c>adhoc_fog_atten_sky</c> and <c>_grd</c>. See <c>SkyPostFxPass</c>.</summary>
    public float AdhocFogAttenSky => GetFloat("AfParam_attenuationForSky", 1f);
    public float AdhocFogAttenGrd => GetFloat("AfParam_attenuationForGrd", 4f);

    /// <summary>Height fog (<c>YFogColor</c>, <c>YFogStart</c>). Parsed but not yet consumed.</summary>
    public Vector3 YFogColor => Xyz(GetColor("YFogColor", Vector4.Zero));
    public float YFogDensity => GetColor("YFogColor", Vector4.Zero).W;
    public float YFogStart => GetFloat("YFogStart", 0f);
    /// <summary>Scatter-fog attenuation (<c>SfParam_attenuation</c>), present verbatim in a captured cloud-shader constant buffer.</summary>
    public float ScatterFogAttenuation => GetFloat("SfParam_attenuation", 40f);
    /// <summary>Scatter-fog horizon exponent (<c>SfParam_horizontal</c>), likewise confirmed in a capture.</summary>
    public float ScatterFogHorizontal => GetFloat("SfParam_horizontal", 2.5f);

    /// <summary>
    /// One of a palette's two cloud layers (<see cref="Cloud0"/>, <see cref="Cloud1"/>): a three-way gradient (shadow,
    /// base, hilight, each with colour and intensity) plus a backlight term for when the sun is behind the cloud.
    /// <see cref="Present"/> is false when the layer is absent or disabled by its <c>Cloud0NoUse</c> / <c>Cloud1NoUse</c>
    /// switch (a clear noon preset authors Cloud0 data but sets <c>Cloud0NoUse</c>).
    /// </summary>
    public readonly record struct CloudLayer(
        bool Present, float BacklightPower,
        Vector3 ColorBackLight, Vector3 ColorBase, Vector3 ColorHilight, Vector3 ColorShadow,
        float IntensityBase, float IntensityHilight, float IntensityShadow);

    CloudLayer GetCloudLayer(string key)
    {
        if (GetBool(key + "NoUse", false))
            return default;
        if (!_raw.TryGetValue(key, out var raw) || raw is not IReadOnlyDictionary<string, object?> map)
            return default;
        var sub = new EnvPalette(key, map);
        return new CloudLayer(
            Present: true,
            BacklightPower: sub.GetFloat("BacklightPower", 1f),
            ColorBackLight: Xyz(sub.GetColor("ColorBackLight", Vector4.One)),
            ColorBase: Xyz(sub.GetColor("ColorBase", Vector4.One)),
            ColorHilight: Xyz(sub.GetColor("ColorHilight", Vector4.One)),
            ColorShadow: Xyz(sub.GetColor("ColorShadow", Vector4.One)),
            IntensityBase: sub.GetFloat("IntensityBase", 1f),
            IntensityHilight: sub.GetFloat("IntensityHilight", 1f),
            IntensityShadow: sub.GetFloat("IntensityShadow", 1f));
    }

    public CloudLayer Cloud0 => GetCloudLayer("Cloud0");
    public CloudLayer Cloud1 => GetCloudLayer("Cloud1");

    public Vector3 VolumeMaskColor => Xyz(GetColor("VolumeMaskColor", Vector4.Zero));
    public float VolumeMaskIntensity => GetFloat("VolumeMaskIntensity", 0f);
    /// <summary>
    /// The palette's "ignore my VolumeMaskColor" switch. The tint it gates is inert regardless: the resolve applies it as
    /// <c>cTex_VolumeMask.z * Env[81].w</c> and an all-zero 1x1 volume mask is bound, since the game's indoor system that
    /// fills it is not rendered.
    /// </summary>
    public bool VolumeMaskColorNoUse => GetBool("VolumeMaskColorNoUse", false);

    public bool BloomEnable => GetBool("BloomEnable", true);
    public float BloomIntensity => GetFloat("BloomIntensity", 0f);
    public float BloomThreshold => GetFloat("BloomThreshold", 1.5f);
    public float BloomClampedLuminance => GetFloat("BloomClampedLuminance", 30f);

    public bool ColorCorrectEnable => GetBool("CC_Enable", true);
    public float ColorCorrectSaturation => GetFloat("CC_Saturation", 1.0f);
    public float ColorCorrectBrightness => GetFloat("CC_Brightness", 1.0f);
    public float ColorCorrectGamma => GetFloat("CC_Gamma", 1.0f);

    /// <summary>Parses one JSON value into the plain-object bag <see cref="EnvPalette"/> reads, for the two code-defined presets.</summary>
    internal static object? FromJson(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => el.EnumerateArray().Select(FromJson).ToArray(),
        JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => FromJson(p.Value)),
        _ => null,
    };

    /// <summary>Parses one <c>Byml</c> node into the same bag. A colour is a <c>{R,G,B,A}</c> map, which is why <see cref="TryGetColor"/> accepts both shapes.</summary>
    internal static object? FromByml(Byml node) => node.Type switch
    {
        BymlNodeType.String => node.GetString(),
        BymlNodeType.Bool => node.GetBool(),
        BymlNodeType.Int => (double)node.GetInt(),
        BymlNodeType.UInt32 => (double)node.GetUInt32(),
        BymlNodeType.Int64 => (double)node.GetInt64(),
        BymlNodeType.UInt64 => (double)node.GetUInt64(),
        BymlNodeType.Float => (double)node.GetFloat(),
        BymlNodeType.Double => node.GetDouble(),
        BymlNodeType.Array => node.GetArray().Select(FromByml).ToArray(),
        BymlNodeType.Map => node.GetMap().ToDictionary(p => p.Key, p => FromByml(p.Value), StringComparer.Ordinal),
        _ => null,
    };
}
