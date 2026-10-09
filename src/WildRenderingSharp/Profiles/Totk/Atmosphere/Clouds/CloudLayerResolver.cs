using System.Numerics;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere.Clouds;

/// <summary>
/// Lays the weather's cloud data over a layer's <c>CloudParamN</c> the way <c>WorldEnvMgr::applyEnvironment</c> does, producing the layer the dome is
/// drawn with at one moment and altitude.
/// </summary>
internal static class CloudLayerResolver
{
    // Texture units per second per unit of ScrollSpd, and per unit of NoiseAdd*, along a unit wind. The game scales both by wind state it computes
    // elsewhere; these are fitted to a capture of its cloud draw, where the noise offsets (0.436, -0.083, -1.034, 1.299) and the scroll offsets
    // (-0.0384, 0.0782) all follow from one wind direction (CapturedWind) and one multiplier each over the same 1107 seconds.
    const float WindScrollUnit = 8.73e-5f;
    const float WindNoiseUnit = 1.74e-4f;

    // The unit wind the capture implies: the ratios of its four noise offsets and of its two scroll offsets both solve to it.
    public static readonly Vector2 CapturedWind = new(0.442f, -0.897f);

    // The layer as drawn, or null if the weather leaves it invisible (no look for it, or no alpha at this altitude).
    public static CloudPostFxLayer? Resolve(
        CloudPostFxLayer baseline, CloudMotionLayer? motion, CloudLookLayer? look, Vector2 wind, double seconds, float altitude)
    {
        if (look is null || !baseline.IsEnable)
            return null;

        var layer = baseline.Copy();
        float visibility = 1f;

        if (motion is not null)
        {
            layer.BaseTextureNo = motion.BaseTextureNo;
            layer.BaseTextureNoBlend = motion.BaseTextureNoBlend;
            layer.NoiseTextureNo = motion.NoiseTextureNo;
            layer.NoiseTextureNoBlend = motion.NoiseTextureNoBlend;
            layer.BaseTexScale = motion.BaseTexScale.At(seconds);
            visibility = motion.AltitudeVisibility(altitude);

            layer.BaseTexScrollSpdX = WindScrollUnit * motion.ScrollSpd * wind.X;
            layer.BaseTexScrollSpdY = WindScrollUnit * motion.ScrollSpd * wind.Y;
            layer.NoiseSpeed1X = Noise(motion.NoiseAdd1, motion.NoiseAdd1Side, wind.X, wind.Y);
            layer.NoiseSpeed1Y = Noise(motion.NoiseAdd1Side, motion.NoiseAdd1, wind.X, wind.Y);
            layer.NoiseSpeed2X = Noise(motion.NoiseAdd2, motion.NoiseAdd2Side, wind.X, wind.Y);
            layer.NoiseSpeed2Y = Noise(motion.NoiseAdd2Side, motion.NoiseAdd2, wind.X, wind.Y);
            layer.NoiseSpeedMaster = 1f;
        }

        layer.AlphaMul = look.AlphaMul.At(seconds) * visibility;
        if (layer.AlphaMul <= 0f)
            return null;

        layer.AlphaThreshold = look.AlphaThreshold.At(seconds);
        layer.Density = look.Density.At(seconds);
        layer.Distotion = look.Distotion.At(seconds);
        layer.SkyHeight = look.SkyHeight.At(seconds);

        layer.EmbossWidth = look.Override("EmbossWidth", layer.EmbossWidth);
        layer.EmbossDensity = look.Override("EmbossDensity", layer.EmbossDensity);
        layer.HilightPower = look.Override("HighlightPower", layer.HilightPower);
        layer.ShadowPower = look.Override("ShadowPower", layer.ShadowPower);
        layer.HighlightRange = look.Override("HighlightRange", layer.HighlightRange);
        layer.HighlightAmbient = look.Override("HighlightAmbient", layer.HighlightAmbient);
        layer.DarkSideNoiseParam = look.Override("DarkSideNoiseParam", layer.DarkSideNoiseParam);
        layer.LightSideNoiseParam = look.Override("LightSideNoiseParam", layer.LightSideNoiseParam);
        layer.BacklightPower = look.Override("BacklightPowe", layer.BacklightPower);
        layer.BacklightRange = look.Override("BacklightRange", layer.BacklightRange);
        layer.BacklightParam0 = look.Override("BacklightParam0", layer.BacklightParam0);
        layer.BacklightParam1 = look.Override("BacklightParam1", layer.BacklightParam1);
        layer.ScatterHeight = look.Override("ScatterHeight", layer.ScatterHeight);
        layer.ScatterAmb = look.Override("ScatterAmbient", layer.ScatterAmb);
        layer.FarUVPow = look.Override("FarUVPow", layer.FarUVPow);
        layer.FarDensityChgStart = look.Override("FarDensityChangeStart", layer.FarDensityChgStart);
        layer.FarDensityChgEnd = look.Override("FarDensityChangeEnd", layer.FarDensityChgEnd);
        layer.FarDensityChgPower = look.Override("FarDensityChangePower", layer.FarDensityChgPower);
        layer.FarAlphaChgStart = look.Override("FarAlphaChgStart", layer.FarAlphaChgStart);
        layer.FarAlphaChgEnd = look.Override("FarAlphaChgEnd", layer.FarAlphaChgEnd);
        layer.FarAlphaChgPower = look.Override("FarAlphaChgPower", layer.FarAlphaChgPower);
        layer.FarDistotionChgStart = look.Override("FarDistotionChgStart", layer.FarDistotionChgStart);
        layer.FarDistotionChgPower = look.Override("FarDistotionChgPower", layer.FarDistotionChgPower);
        return layer;
    }

    // The game's NoiseAdd: the wind's X component times the along-wind term plus its Y component times the side term.
    static float Noise(float along, float side, float windX, float windY) => WindNoiseUnit * (windX * along + windY * side);
}
