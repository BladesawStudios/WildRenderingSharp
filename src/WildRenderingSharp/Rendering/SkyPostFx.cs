using System.Numerics;
using System.Text.Json;
using SarcLibrary;

namespace WildRenderingSharp.Rendering;

/// <summary>
/// The real <c>agl::pfx::Sky</c> config the game itself loads at runtime - <c>postfx/master_field.baglsky</c>
/// inside <c>Env/GameScene.Nin_NX_NVN.genvb.zs</c> (a SARC of AAMP <c>.bagl*</c> files), parsed by
/// the real <c>AampLibrary</c> NuGet package rather than a hand-rolled AAMP reader. The actual
/// parsing happens OUT OF PROCESS-ISOLATION (a private <c>AssemblyLoadContext</c>, not literally a
/// separate OS process) in <c>WildRenderingSharp.AampReader</c> - a standalone project this one does NOT
/// reference at compile time - because <c>AampLibrary</c> needs <c>Syroot.BinaryData</c>/
/// <c>Syroot.Maths</c> 5.x, which is binary-incompatible with the 2.x versions vendored for
/// <c>BfresLibrary</c> (<c>vendor/ShaderLibrary/ShaderLibrary.CompileTool/Libs/Bfres/*.dll</c>) that
/// the SAME process also needs, in-process, for BFRES/material parsing - confirmed by reproduction:
/// adding AampLibrary as a normal PackageReference here broke <c>ViewportPanel</c> construction at
/// startup with <c>TypeLoadException: Could not load type 'Syroot.BinaryData.BinaryDataReader'</c>
/// the moment <c>BfresLibraryPatches.EnsureApplied()</c> ran. See
/// <see cref="WildRenderingSharp.Rendering.IsolatedAampReader"/> for the loader and
/// <c>WildRenderingSharp.AampReader/SkyPostFxJson.cs</c> for the actual AAMP parsing.
///
/// This is a DIFFERENT source from <see cref="EnvPalette"/>: a ResEnvPalette's <c>SkyRParam_*</c>
/// fields are the per-time-of-day/weather DYNAMIC multiplier the game layers on top of this file's
/// STATIC physical baseline (base scattering heights/coefficients, the sun disc's own size/intensity/
/// falloff curve, fog falloff shape, the ground colour seen from orbit) - this file changes only if
/// the ROM itself is patched, a palette changes every few in-game minutes. <see cref="Default"/>
/// mirrors the exact values <c>AmbientLighting</c> previously had hardcoded (transcribed by hand from
/// a one-off dump earlier in this project's life) - <see cref="SkyPostFxLibrary.LoadFromRomfs"/>
/// replaces that hardcoded transcription with a live parse of the real file, falling back to the
/// identical numbers if no romfs is configured or the archive/AAMP reader can't be reached.
/// </summary>
public sealed class SkyPostFx
{
    /// <summary>Per-channel Rayleigh scattering coefficients (why the sky is blue) - real field, unnamed in the AAMP hash table (resolved by <c>WildRenderingSharp.AampReader</c> via its raw CRC32 hash, which is stable regardless of name-table coverage).</summary>
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
    /// <summary>The ground colour seen looking down from the sky, i.e. below the horizon in a screen-space sky pass - a real authored value, not an invented dim.</summary>
    public Vector3 GroundColor = new(0.5f, 0.4f, 0.3f);
    public float ScatterFogNear = 0f;
    public float ScatterFogFar = 30000f;
    public float ScatterFogDensity = 0.85f;
    public float ScatterFogAtten = 40f;
    /// <summary>Real horizon fog falloff curve exponent - how sharply haze thickens as the view ray approaches the horizon.</summary>
    public float ScatterFogHorz = 2.5f;

    // ---- adhoc fog: the coloured haze band that hugs the horizon ----
    // Defaults are master_field.baglsky's own authored values.
    /// <summary>Exponent on the view ray's upward component - shapes how fast the band falls off with elevation. NEVER let this reach the shader as 0: it is a pow() exponent, and pow(0,0) decompiles to exp2(-inf * 0) = NaN.</summary>
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

    /// <summary>Parses the <c>"sky"</c> object of the JSON <see cref="WildRenderingSharp.AampReader.SkyPostFxJson.ParseToJson"/> produces - see that method for the exact key set.</summary>
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

    /// <summary>The 4th component of a colour array - <c>adhoc_fog_color</c> carries the fog DENSITY there, not an opacity.</summary>
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

/// <summary>
/// One <c>CloudParamN</c> block of <c>postfx/master_field.baglclwd</c> - the real <c>agl::fx::Cloud</c>
/// billboard-dome shading model's per-layer parameters, EVERY field (not a hand-picked subset - see
/// <c>WildRenderingSharp.AampReader.SkyPostFxJson.ParseObject</c>, which dumps the whole object generically).
/// Field names match the real AAMP names exactly (including the "m" prefix and the authored typo
/// "Distotion") so they can be cross-referenced directly against the real decompiled
/// <c>agl_cloud.vert</c>/<c>.frag</c> (<c>ModelPreparer.EnsureCloudShader</c>) and its own uniform
/// reflection (<c>mDensity</c>, <c>mAlphaMul</c>, etc.) without a name-mapping table to get out of
/// sync.
///
/// <c>CloudParam2</c> in the shipped file is byte-identical to <c>CloudParam1</c> (confirmed by
/// direct dump) - not a third distinct layer - so <see cref="CloudPostFx"/> only exposes two.
///
/// The <c>*No</c>/<c>*No_Blend</c> fields are texture SLOT INDICES into a small fixed array, not
/// names - traced via Ghidra: index 1 (both <see cref="NoiseTextureNo"/> and
/// <see cref="NoiseTextureNoBlend"/> are always 1 in the real file) resolves through a generic
/// runtime name->texture dispatcher (<c>FUN_71008da38c</c>) to a texture literally named
/// <c>"cloud_noise"</c> that is BAKED, not loaded from romfs - by the real <c>noise_cloud</c> shading
/// model in <c>agl_technique_proc.sharcb</c> (see <c>TestAglShader.ExtractCloudNoiseShader</c>).
/// Indices 0/2 (the base texture) are real static romfs assets whose exact filenames were not
/// found despite a real trace attempt (no literal string anywhere near the load site, unlike
/// "cloud_noise") - callers needing an actual texture should treat those two as unconfirmed.
/// </summary>
public sealed class CloudPostFxLayer
{
    public bool IsEnable = true;
    public int BaseTextureNo;
    public int NoiseTextureNo = 1;
    public bool CloudTexBlend = true;
    public float CloudTexBlendRate = 1f;
    public int BaseTextureNoBlend = 2;
    public int NoiseTextureNoBlend = 1;
    public float ScatterHeight = 6f;
    public float ScatterAmb = 0.1f;
    public float SunOccChkSize = 0.015f;
    public float DarkSideNoiseParam = 1f;
    public float LightSideNoiseParam = 0.25f;
    public float Distotion = 0.65f;
    public float Density = 0.4f;
    public float NoiseSpeedMaster = 1f;
    public float NoiseSpeed1X = 0.25f;
    public float NoiseSpeed1Y = -0.02f;
    public float NoiseSpeed2X = -0.5f;
    public float NoiseSpeed2Y = 0.3f;
    public float NoiseScale1 = 4f;
    public float NoiseScale2 = 8f;
    public float NoiseDensity1 = 0.5f;
    public float NoiseDensity2 = 0.5f;
    public float EmbossWidth = 0.05f;
    public float EmbossDensity = -0.0066f;
    public float HilightPower = 0.01f;
    public float ShadowPower = 0.1f;
    public float HighlightRange = 1.3f;
    public float HighlightAmbient = 0.05f;
    public float AlphaMul = 0.7f;
    public float AlphaThreshold = 0.6f;
    public Vector3 BacklightColor = Vector3.One;
    public float BacklightPower = 1f;
    public float BacklightRange = 0.5f;
    public float BacklightParam0 = 0.25f;
    public float BacklightParam1 = 1.2f;
    public Vector3 BaseColor = Vector3.One;
    public Vector3 HilightColor = Vector3.One;
    public Vector3 ShadowColor = Vector3.One;
    public float BaseColorIntensity = 0.15f;
    public float HilightColorIntensity = 0.2f;
    public float ShadowColorIntensity = 0.075f;
    public float BaseTexScale = 2f;
    public float BaseTexScrollSpdX = -0.00005f;
    public float BaseTexScrollSpdY = 0.0001f;
    public float SkyScale = 26500f;
    public float SkyHeight = 7000f;
    public float SunPosX = 0.59f;
    public float SunPosY = 0.5f;
    public float FarUVPow = 8f;
    public float FarUVMul = 1.8f;
    public float FarDensityChgStart = 0.8f;
    public float FarDensityChgEnd = 0.15f;
    public float FarDensityChgPower = 0.15f;
    public float FarAlphaChgStart = 0.7f;
    public float FarAlphaChgEnd = 0.95f;
    public float FarAlphaChgPower = -0.5f;
    public float FarDistotionChgStart = 0.75f;
    public float FarDistotionChgEnd = 0.85f;
    public float FarDistotionChgPower = 14f;
    public float PosDensityChgX = -0.56f;
    public float PosDensityChgY = -0.38f;
    public float PosDensityChgRange = 0.7f;
    public float PosDensityChgPower = -0.13f;
    public bool UseProcedualTexture;
    public bool UseScatter = true;
    public bool UseDebugDispSun;

    public static readonly CloudPostFxLayer Default = new();

    internal static CloudPostFxLayer FromJson(JsonElement layer)
    {
        var d = Default;
        return new CloudPostFxLayer
        {
            IsEnable = SkyPostFx.ReadBool(layer, "mIsEnable", d.IsEnable),
            BaseTextureNo = SkyPostFx.ReadInt(layer, "mBaseTextureNo", d.BaseTextureNo),
            NoiseTextureNo = SkyPostFx.ReadInt(layer, "mNoiseTextureNo", d.NoiseTextureNo),
            CloudTexBlend = SkyPostFx.ReadBool(layer, "mbCloudTexBlend", d.CloudTexBlend),
            CloudTexBlendRate = SkyPostFx.ReadFloat(layer, "mCloudTexBlendRate", d.CloudTexBlendRate),
            BaseTextureNoBlend = SkyPostFx.ReadInt(layer, "mBaseTextureNo_Blend", d.BaseTextureNoBlend),
            NoiseTextureNoBlend = SkyPostFx.ReadInt(layer, "mNoiseTextureNo_Blend", d.NoiseTextureNoBlend),
            ScatterHeight = SkyPostFx.ReadFloat(layer, "mScatterHeight", d.ScatterHeight),
            ScatterAmb = SkyPostFx.ReadFloat(layer, "mScatterAmb", d.ScatterAmb),
            SunOccChkSize = SkyPostFx.ReadFloat(layer, "mSunOccChkSize", d.SunOccChkSize),
            DarkSideNoiseParam = SkyPostFx.ReadFloat(layer, "mDarkSideNoiseParam", d.DarkSideNoiseParam),
            LightSideNoiseParam = SkyPostFx.ReadFloat(layer, "mLightSideNoiseParam", d.LightSideNoiseParam),
            Distotion = SkyPostFx.ReadFloat(layer, "mDistotion", d.Distotion),
            Density = SkyPostFx.ReadFloat(layer, "mDensity", d.Density),
            NoiseSpeedMaster = SkyPostFx.ReadFloat(layer, "mNoiseSpeedMaster", d.NoiseSpeedMaster),
            NoiseSpeed1X = SkyPostFx.ReadFloat(layer, "mNoiseSpeed1X", d.NoiseSpeed1X),
            NoiseSpeed1Y = SkyPostFx.ReadFloat(layer, "mNoiseSpeed1Y", d.NoiseSpeed1Y),
            NoiseSpeed2X = SkyPostFx.ReadFloat(layer, "mNoiseSpeed2X", d.NoiseSpeed2X),
            NoiseSpeed2Y = SkyPostFx.ReadFloat(layer, "mNoiseSpeed2Y", d.NoiseSpeed2Y),
            NoiseScale1 = SkyPostFx.ReadFloat(layer, "mNoiseScale1", d.NoiseScale1),
            NoiseScale2 = SkyPostFx.ReadFloat(layer, "mNoiseScale2", d.NoiseScale2),
            NoiseDensity1 = SkyPostFx.ReadFloat(layer, "mNoiseDensity1", d.NoiseDensity1),
            NoiseDensity2 = SkyPostFx.ReadFloat(layer, "mNoiseDensity2", d.NoiseDensity2),
            EmbossWidth = SkyPostFx.ReadFloat(layer, "mEmbossWidth", d.EmbossWidth),
            EmbossDensity = SkyPostFx.ReadFloat(layer, "mEmbossDensity", d.EmbossDensity),
            HilightPower = SkyPostFx.ReadFloat(layer, "mHilightPower", d.HilightPower),
            ShadowPower = SkyPostFx.ReadFloat(layer, "mShadowPower", d.ShadowPower),
            HighlightRange = SkyPostFx.ReadFloat(layer, "mHighlightRange", d.HighlightRange),
            HighlightAmbient = SkyPostFx.ReadFloat(layer, "mHighlightAmbient", d.HighlightAmbient),
            AlphaMul = SkyPostFx.ReadFloat(layer, "mAlphaMul", d.AlphaMul),
            AlphaThreshold = SkyPostFx.ReadFloat(layer, "mAlphaThreshold", d.AlphaThreshold),
            BacklightColor = SkyPostFx.ReadColorRgb(layer, "mBacklightColor", d.BacklightColor),
            BacklightPower = SkyPostFx.ReadFloat(layer, "mBacklightPower", d.BacklightPower),
            BacklightRange = SkyPostFx.ReadFloat(layer, "mBacklightRange", d.BacklightRange),
            BacklightParam0 = SkyPostFx.ReadFloat(layer, "mBacklightParam0", d.BacklightParam0),
            BacklightParam1 = SkyPostFx.ReadFloat(layer, "mBacklightParam1", d.BacklightParam1),
            BaseColor = SkyPostFx.ReadColorRgb(layer, "mBaseColor", d.BaseColor),
            HilightColor = SkyPostFx.ReadColorRgb(layer, "mHilightColor", d.HilightColor),
            ShadowColor = SkyPostFx.ReadColorRgb(layer, "mShadowColor", d.ShadowColor),
            BaseColorIntensity = SkyPostFx.ReadFloat(layer, "mBaseColorIntensity", d.BaseColorIntensity),
            HilightColorIntensity = SkyPostFx.ReadFloat(layer, "mHilightColorIntensity", d.HilightColorIntensity),
            ShadowColorIntensity = SkyPostFx.ReadFloat(layer, "mShadowColorIntensity", d.ShadowColorIntensity),
            BaseTexScale = SkyPostFx.ReadFloat(layer, "mBaseTexScale", d.BaseTexScale),
            BaseTexScrollSpdX = SkyPostFx.ReadFloat(layer, "mBaseTexScrollSpdX", d.BaseTexScrollSpdX),
            BaseTexScrollSpdY = SkyPostFx.ReadFloat(layer, "mBaseTexScrollSpdY", d.BaseTexScrollSpdY),
            SkyScale = SkyPostFx.ReadFloat(layer, "mSkyScale", d.SkyScale),
            SkyHeight = SkyPostFx.ReadFloat(layer, "mSkyHeight", d.SkyHeight),
            SunPosX = SkyPostFx.ReadFloat(layer, "mSunPosX", d.SunPosX),
            SunPosY = SkyPostFx.ReadFloat(layer, "mSunPosY", d.SunPosY),
            FarUVPow = SkyPostFx.ReadFloat(layer, "mFarUVPow", d.FarUVPow),
            FarUVMul = SkyPostFx.ReadFloat(layer, "mFarUVMul", d.FarUVMul),
            FarDensityChgStart = SkyPostFx.ReadFloat(layer, "mFarDensityChgStart", d.FarDensityChgStart),
            FarDensityChgEnd = SkyPostFx.ReadFloat(layer, "mFarDensityChgEnd", d.FarDensityChgEnd),
            FarDensityChgPower = SkyPostFx.ReadFloat(layer, "mFarDensityChgPower", d.FarDensityChgPower),
            FarAlphaChgStart = SkyPostFx.ReadFloat(layer, "mFarAlphaChgStart", d.FarAlphaChgStart),
            FarAlphaChgEnd = SkyPostFx.ReadFloat(layer, "mFarAlphaChgEnd", d.FarAlphaChgEnd),
            FarAlphaChgPower = SkyPostFx.ReadFloat(layer, "mFarAlphaChgPower", d.FarAlphaChgPower),
            FarDistotionChgStart = SkyPostFx.ReadFloat(layer, "mFarDistotionChgStart", d.FarDistotionChgStart),
            FarDistotionChgEnd = SkyPostFx.ReadFloat(layer, "mFarDistotionChgEnd", d.FarDistotionChgEnd),
            FarDistotionChgPower = SkyPostFx.ReadFloat(layer, "mFarDistotionChgPower", d.FarDistotionChgPower),
            PosDensityChgX = SkyPostFx.ReadFloat(layer, "mPosDensityChgX", d.PosDensityChgX),
            PosDensityChgY = SkyPostFx.ReadFloat(layer, "mPosDensityChgY", d.PosDensityChgY),
            PosDensityChgRange = SkyPostFx.ReadFloat(layer, "mPosDensityChgRange", d.PosDensityChgRange),
            PosDensityChgPower = SkyPostFx.ReadFloat(layer, "mPosDensityChgPower", d.PosDensityChgPower),
            UseProcedualTexture = SkyPostFx.ReadBool(layer, "mUseProcedualTexture", d.UseProcedualTexture),
            UseScatter = SkyPostFx.ReadBool(layer, "mUseScatter", d.UseScatter),
            UseDebugDispSun = SkyPostFx.ReadBool(layer, "mUseDebugDispSun", d.UseDebugDispSun),
        };
    }
}

/// <summary>The real <c>agl::fx::Cloud</c> top-level "Cloud" object - settings shared by both layers (as opposed to <see cref="CloudPostFxLayer"/>'s per-layer settings).</summary>
public sealed class CloudPostFxShared
{
    public bool IsEnable = true;
    public bool IsDrawReduceBuffer = true;
    public bool IsDisableFarClip = true;
    public bool IsDisableDepthTest;
    public int DrawOrder;
    public float CloudColorScale = 1f;
    public Vector3 FogColor = new(0.4f, 0.6f, 0.9f);
    public float FogNear = -5f;
    public float FogFar = 300f;
    public float AttenuationForSky = 0.5f;
    public int SunOccBufSize = 8;

    public static readonly CloudPostFxShared Default = new();

    internal static CloudPostFxShared FromJson(JsonElement obj)
    {
        var d = Default;
        return new CloudPostFxShared
        {
            IsEnable = SkyPostFx.ReadBool(obj, "IsEnable", d.IsEnable),
            IsDrawReduceBuffer = SkyPostFx.ReadBool(obj, "mIsDrawReduceBuffer", d.IsDrawReduceBuffer),
            IsDisableFarClip = SkyPostFx.ReadBool(obj, "mIsDisableFarClip", d.IsDisableFarClip),
            IsDisableDepthTest = SkyPostFx.ReadBool(obj, "mIsDisableDepthTest", d.IsDisableDepthTest),
            DrawOrder = SkyPostFx.ReadInt(obj, "mDrawOrder", d.DrawOrder),
            CloudColorScale = SkyPostFx.ReadFloat(obj, "mCloudColorScale", d.CloudColorScale),
            FogColor = SkyPostFx.ReadColorRgb(obj, "mFogColor", d.FogColor),
            FogNear = SkyPostFx.ReadFloat(obj, "mFogNear", d.FogNear),
            FogFar = SkyPostFx.ReadFloat(obj, "mFogFar", d.FogFar),
            AttenuationForSky = SkyPostFx.ReadFloat(obj, "mAttenuationForSky", d.AttenuationForSky),
            SunOccBufSize = SkyPostFx.ReadInt(obj, "mSunOccBufSize", d.SunOccBufSize),
        };
    }
}

public sealed class CloudPostFx
{
    public CloudPostFxShared Shared = CloudPostFxShared.Default;
    public CloudPostFxLayer Layer0 = CloudPostFxLayer.Default;
    public CloudPostFxLayer Layer1 = CloudPostFxLayer.Default;

    public static readonly CloudPostFx Default = new();

    internal static CloudPostFx FromJson(JsonElement cloud)
    {
        var result = new CloudPostFx();
        if (cloud.TryGetProperty("cloud", out var shared)) result.Shared = CloudPostFxShared.FromJson(shared);
        if (cloud.TryGetProperty("layer0", out var l0)) result.Layer0 = CloudPostFxLayer.FromJson(l0);
        if (cloud.TryGetProperty("layer1", out var l1)) result.Layer1 = CloudPostFxLayer.FromJson(l1);
        return result;
    }
}

/// <summary>
/// Loads <see cref="SkyPostFx"/>/<see cref="CloudPostFx"/> once from <c>Env/GameScene.Nin_NX_NVN.genvb.zs</c>
/// - mirrors <see cref="EnvPaletteLibrary.LoadFromRomfs"/>'s own SARC/Zstd opening pattern exactly
/// (same archive family, same <c>TotkCommon</c> helpers) for locating and decompressing the archive,
/// then hands the two extracted <c>.bagl*</c> byte blobs to <see cref="IsolatedAampReader"/> (see its
/// own remarks for why the actual AAMP parsing can't happen directly in this assembly). Cached
/// process-wide since this data is entirely static (no per-palette variation, unlike <c>EnvPalette</c>).
/// </summary>
public static class SkyPostFxLibrary
{
    static (SkyPostFx Sky, CloudPostFx Cloud, ColorCorrectionPostFx ColorCorrection)? _cached;


    /// <summary>Reads the generic <c>color_correction</c> object into <see cref="ColorCorrectionPostFx"/>.</summary>
    static ColorCorrectionPostFx ColorCorrectionFromJson(JsonElement el)
    {
        var cc = new ColorCorrectionPostFx();
        float F(string k, float d) => el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : d;
        bool B(string k, bool d) => el.TryGetProperty(k, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : d;
        Vector3 C(string k, Vector3 d)
        {
            if (!el.TryGetProperty(k, out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() < 3)
                return d;
            return new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());
        }

        cc.Enable = B("enable", cc.Enable);
        cc.Hue = F("hue", cc.Hue);
        cc.Saturation = F("saturation", cc.Saturation);
        cc.Brightness = F("brightness", cc.Brightness);
        cc.Gamma = F("gamma", cc.Gamma);
        cc.OrderToycamHsb = B("order_toycam_hsb", cc.OrderToycamHsb);
        cc.ToycamEnable = B("toycam_enable", cc.ToycamEnable);
        cc.ToycamOffset1 = C("toycam_offset1", cc.ToycamOffset1);
        cc.ToycamOffset2 = C("toycam_offset2", cc.ToycamOffset2);
        cc.ToycamLevel1 = C("toycam_level1", cc.ToycamLevel1);
        cc.ToycamLevel2 = C("toycam_level2", cc.ToycamLevel2);
        cc.ToycamSaturation1 = F("toycam_saturation1", cc.ToycamSaturation1);
        cc.ToycamSaturation2 = F("toycam_saturation2", cc.ToycamSaturation2);
        cc.ToycamBrightness = F("toycam_brightness", cc.ToycamBrightness);
        cc.ToycamContrast = F("toycam_contrast", cc.ToycamContrast);
        cc.ToycamMulColor = C("toycam_mul_color", cc.ToycamMulColor);
        return cc;
    }

    public static (SkyPostFx Sky, CloudPostFx Cloud, ColorCorrectionPostFx ColorCorrection) LoadFromRomfs(string? romfsRoot)
    {
        if (_cached is { } cached)
            return cached;

        var fallback = (Sky: SkyPostFx.Default, Cloud: CloudPostFx.Default, ColorCorrection: ColorCorrectionPostFx.Default);
        if (string.IsNullOrEmpty(romfsRoot))
            return fallback;

        string genvbPath = Path.Combine(romfsRoot, "Env", "GameScene.Nin_NX_NVN.genvb.zs");
        if (!File.Exists(genvbPath))
        {
            Console.WriteLine($"[SkyPostFxLibrary] no GameScene.Nin_NX_NVN.genvb.zs under '{romfsRoot}' - TotK Sky background falls back to hand-transcribed defaults.");
            return fallback;
        }

        try
        {
            byte[] raw = File.ReadAllBytes(genvbPath);
            byte[] decompressed = TotkCommon.Zstd.IsCompressed(raw) ? TotkCommon.Totk.Zstd.Decompress(raw) : raw;
            var sarc = Sarc.FromBinary(new ArraySegment<byte>(decompressed));

            byte[]? skyBytes = null, cloudBytes = null, ccrBytes = null;
            foreach (var (path, data) in sarc)
            {
                if (path == "postfx/master_field.baglsky") skyBytes = data.ToArray();
                else if (path == "postfx/master_field.baglclwd") cloudBytes = data.ToArray();
                else if (path == "postfx/master_field.baglccr") ccrBytes = data.ToArray();
            }
            if (skyBytes is null) Console.WriteLine("[SkyPostFxLibrary] 'postfx/master_field.baglsky' not found in genvb archive - using defaults for sky.");
            if (cloudBytes is null) Console.WriteLine("[SkyPostFxLibrary] 'postfx/master_field.baglclwd' not found in genvb archive - using defaults for clouds.");

            string? json = IsolatedAampReader.TryParseToJson(skyBytes, cloudBytes, ccrBytes);
            if (json is null)
            {
                Console.WriteLine("[SkyPostFxLibrary] real AAMP parse unavailable - using hand-transcribed defaults.");
                return fallback;
            }

            using var doc = JsonDocument.Parse(json);
            var sky = doc.RootElement.TryGetProperty("sky", out var skyEl) ? SkyPostFx.FromJson(skyEl) : SkyPostFx.Default;
            var cloud = doc.RootElement.TryGetProperty("cloud", out var cloudEl) ? CloudPostFx.FromJson(cloudEl) : CloudPostFx.Default;

            var cc = doc.RootElement.TryGetProperty("colorCorrection", out var ccEl)
                ? ColorCorrectionFromJson(ccEl) : ColorCorrectionPostFx.Default;

            Console.WriteLine($"[SkyPostFxLibrary] loaded real sky/cloud postfx from romfs " +
                $"(colour correction: enable={cc.Enable} saturation={cc.Saturation:G4} brightness={cc.Brightness:G4} gamma={cc.Gamma:G4} toycam={cc.ToycamEnable}).");
            _cached = (sky, cloud, cc);
            return _cached.Value;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SkyPostFxLibrary] failed to load real postfx from romfs, using defaults: {ex.Message}");
            return fallback;
        }
    }
}
