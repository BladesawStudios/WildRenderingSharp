using System.Numerics;
using System.Text.Json;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;

/// <summary>
/// The <c>agl::pfx::Sky</c> config the game loads at runtime: <c>postfx/master_field.baglsky</c> inside
/// <c>Env/GameScene.Nin_NX_NVN.genvb.zs</c> (a SARC of AAMP <c>.bagl*</c> files).
/// </summary>
public sealed class SkyPostFx
{
    public Vector3 RayleighScatteringCoeff = new(0.0041f, 0.0113f, 0.0284f);
    public float RayleighBaseHeight = 24f;
    public float MieBaseHeight = 2f;
    public float MieScatteringCoeff = 0.00183f;
    public float MieSymmetricalPropRendering = 0.85f;
    public float RayleighAmplifierRendering = 1f;
    public float MieAmplifierRendering = 8f;
    public Vector3 SunColor = new(1f, 0.86f, 0.68f);
    public float RenderSunIntensity = 100f;
    public float RenderSunSize = 1f;
    public float RenderSunLerp = 1f;
    public Vector3 GroundColor = new(0.5f, 0.4f, 0.3f);
    public float ScatterFogNear = 0f;
    public float ScatterFogFar = 30000f;
    public float ScatterFogDensity = 0.85f;
    public float ScatterFogAtten = 40f;
    public float ScatterFogHorz = 2.5f;

    // Adhoc fog: the coloured haze band at the horizon. Defaults are master_field.baglsky's authored values.
    public float AdhocFogAttenSky = 0.7642950f;
    public float AdhocFogAttenMinScaleSky = 0.3f;
    public float AdhocFogAttenGrd = 4f;
    public float AdhocFogNear;
    public float AdhocFogFar = 300f;
    public Vector3 AdhocFogColor = new(0.5f, 0.5f, 0.5f);
    public float AdhocFogDensity;

    public static readonly SkyPostFx Default = new();

    internal static SkyPostFx FromJson(JsonElement sky)
    {
        var result = new SkyPostFx();
        result.RayleighScatteringCoeff = ReadVec3(sky, "rayleigh_scattering_coeff", result.RayleighScatteringCoeff);
        result.RayleighBaseHeight = ReadFloat(sky, "rayleigh_base_height", result.RayleighBaseHeight);
        result.MieBaseHeight = ReadFloat(sky, "mie_base_height", result.MieBaseHeight);
        result.MieScatteringCoeff = ReadFloat(sky, "mie_scattering_coeff", result.MieScatteringCoeff);
        result.MieSymmetricalPropRendering = ReadFloat(sky, "mie_symmetrical_prop_rendering", result.MieSymmetricalPropRendering);
        result.RayleighAmplifierRendering = ReadFloat(sky, "rayleigh_amplifier_rendering", result.RayleighAmplifierRendering);
        result.MieAmplifierRendering = ReadFloat(sky, "mie_amplifier_rendering", result.MieAmplifierRendering);
        result.SunColor = ReadColorRgb(sky, "sun_color", result.SunColor);
        result.RenderSunIntensity = ReadFloat(sky, "render_sun_intensity", result.RenderSunIntensity);
        result.RenderSunSize = ReadFloat(sky, "render_sun_size", result.RenderSunSize);
        result.RenderSunLerp = ReadFloat(sky, "render_sun_lerp", result.RenderSunLerp);
        result.GroundColor = ReadColorRgb(sky, "ground_color", result.GroundColor);
        result.ScatterFogNear = ReadFloat(sky, "scatter_fog_near", result.ScatterFogNear);
        result.ScatterFogFar = ReadFloat(sky, "scatter_fog_far", result.ScatterFogFar);
        result.ScatterFogDensity = ReadFloat(sky, "scatter_fog_density", result.ScatterFogDensity);
        result.ScatterFogAtten = ReadFloat(sky, "scatter_fog_atten", result.ScatterFogAtten);
        result.ScatterFogHorz = ReadFloat(sky, "scatter_fog_horz", result.ScatterFogHorz);
        result.AdhocFogAttenSky = ReadFloat(sky, "adhoc_fog_atten_sky", result.AdhocFogAttenSky);
        result.AdhocFogAttenMinScaleSky = ReadFloat(sky, "adhoc_fog_atten_minscale_sky", result.AdhocFogAttenMinScaleSky);
        result.AdhocFogAttenGrd = ReadFloat(sky, "adhoc_fog_atten_grd", result.AdhocFogAttenGrd);
        result.AdhocFogNear = ReadFloat(sky, "adhoc_fog_near", result.AdhocFogNear);
        result.AdhocFogFar = ReadFloat(sky, "adhoc_fog_far", result.AdhocFogFar);
        result.AdhocFogColor = ReadColorRgb(sky, "adhoc_fog_color", result.AdhocFogColor);
        result.AdhocFogDensity = ReadColorAlpha(sky, "adhoc_fog_color", result.AdhocFogDensity);
        return result;
    }

    internal static float ReadFloat(JsonElement obj, string name, float fallback) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;

    internal static bool ReadBool(JsonElement obj, string name, bool fallback) =>
        obj.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : fallback;

    internal static int ReadInt(JsonElement obj, string name, int fallback) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;

    static Vector3 ReadVec3(JsonElement obj, string name, Vector3 fallback)
    {
        if (!obj.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() < 3)
            return fallback;
        return new Vector3(arr[0].GetSingle(), arr[1].GetSingle(), arr[2].GetSingle());
    }

    internal static float ReadColorAlpha(JsonElement obj, string name, float fallback)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() >= 4)
            return v[3].GetSingle();
        return fallback;
    }

    internal static Vector3 ReadColorRgb(JsonElement obj, string name, Vector3 fallback)
    {
        if (!obj.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() < 3)
            return fallback;
        return new Vector3(arr[0].GetSingle(), arr[1].GetSingle(), arr[2].GetSingle());
    }
}
