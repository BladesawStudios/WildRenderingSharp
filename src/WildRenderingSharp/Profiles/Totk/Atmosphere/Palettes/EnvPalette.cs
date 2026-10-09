using System.Numerics;
using System.Text.Json;
using BymlLibrary;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;

/// <summary>
/// A <c>game::wm::ResEnvPalette</c> record: the per-time-of-day and weather parameter set the runtime feeds the renderer, loaded
/// from romfs (<c>Pack/EnvPalette.pack.zs</c>; see <see cref="EnvPaletteLibrary.Load"/>).
/// </summary>
public sealed class EnvPalette
{
    readonly IReadOnlyDictionary<string, object?> _raw;

    public string Name { get; }

    public EnvPalette(string name, IReadOnlyDictionary<string, object?> raw)
    {
        Name = name;
        _raw = raw;
    }

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

    public float AmbientScale => GetFloat("AmbientScale", 1f);

    // Sky scattering: what the game feeds its own sky shader, and what the ambient approximation uses (see AmbientLighting).

    public float SkyRayleighAmplifier => GetFloat("SkyRParam_rayleigh_amplifier", 1f);
    public float SkyMieAmplifier => GetFloat("SkyRParam_mie_amplifier", 0f);
    public float SkyMieSymmetrical => GetFloat("SkyRParam_mie_symmetrical", 0f);
    public Vector3 SkySunColor => Xyz(GetColor("SkySunColor", new Vector4(1, 1, 1, 1)));
    public float SkySunColorIntensity => GetFloat("SkySunColorIntensity", 1f);
    public bool SkySunColorNoUse => GetBool("SkySunColorNoUse", false);

    public Vector3 FogColor => Xyz(GetColor("FogColor", new Vector4(0.6f, 0.75f, 1f, 0f)));

    public float FogEnd => GetFloat("FogEnd", 300f);
    public float FogStart => GetFloat("FogStart", 0f);

    public float FogDensity => GetColor("FogColor", new Vector4(0.6f, 0.75f, 1f, 0f)).W;

    public float AdhocFogAttenSky => GetFloat("AfParam_attenuationForSky", 1f);
    public float AdhocFogAttenGrd => GetFloat("AfParam_attenuationForGrd", 4f);

    public Vector3 YFogColor => Xyz(GetColor("YFogColor", Vector4.Zero));
    public float YFogDensity => GetColor("YFogColor", Vector4.Zero).W;
    public float YFogStart => GetFloat("YFogStart", 0f);
    public float ScatterFogAttenuation => GetFloat("SfParam_attenuation", 40f);
    public float ScatterFogHorizontal => GetFloat("SfParam_horizontal", 2.5f);

    // One of a palette's two cloud layers (Cloud0, Cloud1): a three-way gradient (shadow, base, hilight, each with colour and intensity) plus a
    // backlight term for when the sun is behind the cloud.
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
    public bool VolumeMaskColorNoUse => GetBool("VolumeMaskColorNoUse", false);

    public bool BloomEnable => GetBool("BloomEnable", true);
    public float BloomIntensity => GetFloat("BloomIntensity", 0f);
    public float BloomThreshold => GetFloat("BloomThreshold", 1.5f);
    public float BloomClampedLuminance => GetFloat("BloomClampedLuminance", 30f);

    public bool ColorCorrectEnable => GetBool("CC_Enable", true);
    public float ColorCorrectSaturation => GetFloat("CC_Saturation", 1.0f);
    public float ColorCorrectBrightness => GetFloat("CC_Brightness", 1.0f);
    public float ColorCorrectGamma => GetFloat("CC_Gamma", 1.0f);

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

    // Parses one Byml node into the same bag. A colour is a {R,G,B,A} map, which is why TryGetColor accepts both shapes.
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
