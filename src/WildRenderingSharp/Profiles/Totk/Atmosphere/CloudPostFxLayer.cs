using System.Numerics;
using System.Text.Json;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>
/// One <c>CloudParamN</c> block of <c>postfx/master_field.baglclwd</c>: the per-layer parameters of the <c>agl::fx::Cloud</c>
/// billboard-dome shading model, every field (see <c>WildRenderingSharp.AampReader.SkyPostFxJson.ParseObject</c>, which dumps the
/// object generically).
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

    public CloudPostFxLayer Copy() => (CloudPostFxLayer)MemberwiseClone();

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
