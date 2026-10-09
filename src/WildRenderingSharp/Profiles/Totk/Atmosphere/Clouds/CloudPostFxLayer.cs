using System.Numerics;
using System.Text.Json;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere.Clouds;

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
        var result = Default.Copy();
        result.ReadTextures(layer);
        result.ReadNoise(layer);
        result.ReadShading(layer);
        result.ReadColors(layer);
        result.ReadDome(layer);
        result.ReadFades(layer);
        result.ReadSwitches(layer);
        return result;
    }

    // Which textures the layer samples and how it blends between two sets.
    void ReadTextures(JsonElement layer)
    {
        IsEnable = SkyPostFx.ReadBool(layer, "mIsEnable", IsEnable);
        BaseTextureNo = SkyPostFx.ReadInt(layer, "mBaseTextureNo", BaseTextureNo);
        NoiseTextureNo = SkyPostFx.ReadInt(layer, "mNoiseTextureNo", NoiseTextureNo);
        CloudTexBlend = SkyPostFx.ReadBool(layer, "mbCloudTexBlend", CloudTexBlend);
        CloudTexBlendRate = SkyPostFx.ReadFloat(layer, "mCloudTexBlendRate", CloudTexBlendRate);
        BaseTextureNoBlend = SkyPostFx.ReadInt(layer, "mBaseTextureNo_Blend", BaseTextureNoBlend);
        NoiseTextureNoBlend = SkyPostFx.ReadInt(layer, "mNoiseTextureNo_Blend", NoiseTextureNoBlend);
    }

    // The scatter terms and the two scrolling noise layers.
    void ReadNoise(JsonElement layer)
    {
        ScatterHeight = SkyPostFx.ReadFloat(layer, "mScatterHeight", ScatterHeight);
        ScatterAmb = SkyPostFx.ReadFloat(layer, "mScatterAmb", ScatterAmb);
        SunOccChkSize = SkyPostFx.ReadFloat(layer, "mSunOccChkSize", SunOccChkSize);
        DarkSideNoiseParam = SkyPostFx.ReadFloat(layer, "mDarkSideNoiseParam", DarkSideNoiseParam);
        LightSideNoiseParam = SkyPostFx.ReadFloat(layer, "mLightSideNoiseParam", LightSideNoiseParam);
        Distotion = SkyPostFx.ReadFloat(layer, "mDistotion", Distotion);
        Density = SkyPostFx.ReadFloat(layer, "mDensity", Density);
        NoiseSpeedMaster = SkyPostFx.ReadFloat(layer, "mNoiseSpeedMaster", NoiseSpeedMaster);
        NoiseSpeed1X = SkyPostFx.ReadFloat(layer, "mNoiseSpeed1X", NoiseSpeed1X);
        NoiseSpeed1Y = SkyPostFx.ReadFloat(layer, "mNoiseSpeed1Y", NoiseSpeed1Y);
        NoiseSpeed2X = SkyPostFx.ReadFloat(layer, "mNoiseSpeed2X", NoiseSpeed2X);
        NoiseSpeed2Y = SkyPostFx.ReadFloat(layer, "mNoiseSpeed2Y", NoiseSpeed2Y);
        NoiseScale1 = SkyPostFx.ReadFloat(layer, "mNoiseScale1", NoiseScale1);
        NoiseScale2 = SkyPostFx.ReadFloat(layer, "mNoiseScale2", NoiseScale2);
        NoiseDensity1 = SkyPostFx.ReadFloat(layer, "mNoiseDensity1", NoiseDensity1);
        NoiseDensity2 = SkyPostFx.ReadFloat(layer, "mNoiseDensity2", NoiseDensity2);
    }

    // Emboss, highlight, shadow and alpha shaping.
    void ReadShading(JsonElement layer)
    {
        EmbossWidth = SkyPostFx.ReadFloat(layer, "mEmbossWidth", EmbossWidth);
        EmbossDensity = SkyPostFx.ReadFloat(layer, "mEmbossDensity", EmbossDensity);
        HilightPower = SkyPostFx.ReadFloat(layer, "mHilightPower", HilightPower);
        ShadowPower = SkyPostFx.ReadFloat(layer, "mShadowPower", ShadowPower);
        HighlightRange = SkyPostFx.ReadFloat(layer, "mHighlightRange", HighlightRange);
        HighlightAmbient = SkyPostFx.ReadFloat(layer, "mHighlightAmbient", HighlightAmbient);
        AlphaMul = SkyPostFx.ReadFloat(layer, "mAlphaMul", AlphaMul);
        AlphaThreshold = SkyPostFx.ReadFloat(layer, "mAlphaThreshold", AlphaThreshold);
    }

    // The backlight, base, highlight and shadow colours and their intensities.
    void ReadColors(JsonElement layer)
    {
        BacklightColor = SkyPostFx.ReadColorRgb(layer, "mBacklightColor", BacklightColor);
        BacklightPower = SkyPostFx.ReadFloat(layer, "mBacklightPower", BacklightPower);
        BacklightRange = SkyPostFx.ReadFloat(layer, "mBacklightRange", BacklightRange);
        BacklightParam0 = SkyPostFx.ReadFloat(layer, "mBacklightParam0", BacklightParam0);
        BacklightParam1 = SkyPostFx.ReadFloat(layer, "mBacklightParam1", BacklightParam1);
        BaseColor = SkyPostFx.ReadColorRgb(layer, "mBaseColor", BaseColor);
        HilightColor = SkyPostFx.ReadColorRgb(layer, "mHilightColor", HilightColor);
        ShadowColor = SkyPostFx.ReadColorRgb(layer, "mShadowColor", ShadowColor);
        BaseColorIntensity = SkyPostFx.ReadFloat(layer, "mBaseColorIntensity", BaseColorIntensity);
        HilightColorIntensity = SkyPostFx.ReadFloat(layer, "mHilightColorIntensity", HilightColorIntensity);
        ShadowColorIntensity = SkyPostFx.ReadFloat(layer, "mShadowColorIntensity", ShadowColorIntensity);
    }

    // The base texture scroll, the dome size and height, and where the sun sits on it.
    void ReadDome(JsonElement layer)
    {
        BaseTexScale = SkyPostFx.ReadFloat(layer, "mBaseTexScale", BaseTexScale);
        BaseTexScrollSpdX = SkyPostFx.ReadFloat(layer, "mBaseTexScrollSpdX", BaseTexScrollSpdX);
        BaseTexScrollSpdY = SkyPostFx.ReadFloat(layer, "mBaseTexScrollSpdY", BaseTexScrollSpdY);
        SkyScale = SkyPostFx.ReadFloat(layer, "mSkyScale", SkyScale);
        SkyHeight = SkyPostFx.ReadFloat(layer, "mSkyHeight", SkyHeight);
        SunPosX = SkyPostFx.ReadFloat(layer, "mSunPosX", SunPosX);
        SunPosY = SkyPostFx.ReadFloat(layer, "mSunPosY", SunPosY);
    }

    // How density, alpha and distortion change toward the horizon and across the dome.
    void ReadFades(JsonElement layer)
    {
        FarUVPow = SkyPostFx.ReadFloat(layer, "mFarUVPow", FarUVPow);
        FarUVMul = SkyPostFx.ReadFloat(layer, "mFarUVMul", FarUVMul);
        FarDensityChgStart = SkyPostFx.ReadFloat(layer, "mFarDensityChgStart", FarDensityChgStart);
        FarDensityChgEnd = SkyPostFx.ReadFloat(layer, "mFarDensityChgEnd", FarDensityChgEnd);
        FarDensityChgPower = SkyPostFx.ReadFloat(layer, "mFarDensityChgPower", FarDensityChgPower);
        FarAlphaChgStart = SkyPostFx.ReadFloat(layer, "mFarAlphaChgStart", FarAlphaChgStart);
        FarAlphaChgEnd = SkyPostFx.ReadFloat(layer, "mFarAlphaChgEnd", FarAlphaChgEnd);
        FarAlphaChgPower = SkyPostFx.ReadFloat(layer, "mFarAlphaChgPower", FarAlphaChgPower);
        FarDistotionChgStart = SkyPostFx.ReadFloat(layer, "mFarDistotionChgStart", FarDistotionChgStart);
        FarDistotionChgEnd = SkyPostFx.ReadFloat(layer, "mFarDistotionChgEnd", FarDistotionChgEnd);
        FarDistotionChgPower = SkyPostFx.ReadFloat(layer, "mFarDistotionChgPower", FarDistotionChgPower);
        PosDensityChgX = SkyPostFx.ReadFloat(layer, "mPosDensityChgX", PosDensityChgX);
        PosDensityChgY = SkyPostFx.ReadFloat(layer, "mPosDensityChgY", PosDensityChgY);
        PosDensityChgRange = SkyPostFx.ReadFloat(layer, "mPosDensityChgRange", PosDensityChgRange);
        PosDensityChgPower = SkyPostFx.ReadFloat(layer, "mPosDensityChgPower", PosDensityChgPower);
    }

    // The procedural-texture, scatter and sun-debug switches.
    void ReadSwitches(JsonElement layer)
    {
        UseProcedualTexture = SkyPostFx.ReadBool(layer, "mUseProcedualTexture", UseProcedualTexture);
        UseScatter = SkyPostFx.ReadBool(layer, "mUseScatter", UseScatter);
        UseDebugDispSun = SkyPostFx.ReadBool(layer, "mUseDebugDispSun", UseDebugDispSun);
    }
}
