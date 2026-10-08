using System.Numerics;
using System.Text.Json;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>
/// The <c>agl::pfx::Sky</c> config the game loads at runtime: <c>postfx/master_field.baglsky</c> inside <c>Env/GameScene.Nin_NX_NVN.genvb.zs</c> (a SARC of AAMP <c>.bagl*</c> files).
/// </summary>
/// <remarks>
/// <para>
/// This is a different source from <see cref="EnvPalette"/>: a palette's <c>SkyRParam_*</c> fields are the dynamic per-time-of-day multiplier the game layers on this file's static
/// physical baseline (scattering heights and coefficients, the sun disc's size and falloff, fog falloff shape, the ground colour seen from orbit). <see cref="Default"/> holds the
/// values transcribed by hand from a one-off dump; <see cref="SkyPostFxLibrary.LoadFromRomfs"/> replaces them with a live parse, falling back to the same numbers if no romfs is
/// configured or the reader cannot be reached.
/// </para>
/// </remarks>
public sealed class SkyPostFx
{
    /// <summary>Per-channel Rayleigh scattering coefficients (why the sky is blue). The field is unnamed in the AAMP hash table; <c>WildRenderingSharp.AampReader</c> resolves it by raw CRC32 hash.</summary>
    public Vector3 RayleighScatteringCoeff = new(0.0041f, 0.0113f, 0.0284f);
    public float RayleighBaseHeight = 24f;
    public float MieBaseHeight = 2f;
    public float MieScatteringCoeff = 0.00183f;
    public float MieSymmetricalPropRendering = 0.85f;
    public float RayleighAmplifierRendering = 1f;
    public float MieAmplifierRendering = 8f;
    public Vector3 SunColor = new(1f, 0.86f, 0.68f);
    public float RenderSunIntensity = 100f;
    /// <summary>Angular size of the drawn sun disc, in the game's own authored unit (1.0 = default).</summary>
    public float RenderSunSize = 1f;
    /// <summary>Blend weight between a hard disc and a soft glow falloff (1.0 = default).</summary>
    public float RenderSunLerp = 1f;
    /// <summary>The ground colour seen looking down from the sky, i.e. below the horizon in a screen-space sky pass.</summary>
    public Vector3 GroundColor = new(0.5f, 0.4f, 0.3f);
    public float ScatterFogNear = 0f;
    public float ScatterFogFar = 30000f;
    public float ScatterFogDensity = 0.85f;
    public float ScatterFogAtten = 40f;
    /// <summary>Horizon fog falloff exponent: how sharply haze thickens as the view ray approaches the horizon.</summary>
    public float ScatterFogHorz = 2.5f;

    // Adhoc fog: the coloured haze band at the horizon. Defaults are master_field.baglsky's authored values.
    /// <summary>Exponent on the view ray's upward component, shaping how fast the band falls off with elevation. Never let this reach the shader as 0: it is a pow() exponent and pow(0,0) decompiles to exp2(-inf * 0) = NaN.</summary>
    public float AdhocFogAttenSky = 0.7642950f;
    /// <summary>Fog scale at the ZENITH; the horizon end of the same mix is the density itself.</summary>
    public float AdhocFogAttenMinScaleSky = 0.3f;
    /// <summary>Ground pass (<c>sky_postfx_ground</c>) only - distance fog over real geometry, not the sky band.</summary>
    public float AdhocFogAttenGrd = 4f;
    public float AdhocFogNear;
    public float AdhocFogFar = 300f;
    public Vector3 AdhocFogColor = new(0.5f, 0.5f, 0.5f);
    /// <summary>The static file's authored density (<c>adhoc_fog_color</c>'s alpha). A palette's own FogColor alpha overrides it per frame.</summary>
    public float AdhocFogDensity;

    public static readonly SkyPostFx Default = new();

    /// <summary>Parses the <c>"sky"</c> object of the JSON <c>SkyPostFxJson.ParseToJson</c> produces.</summary>
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

    /// <summary>The 4th component of a colour array; <c>adhoc_fog_color</c> carries the fog density there, not an opacity.</summary>
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
