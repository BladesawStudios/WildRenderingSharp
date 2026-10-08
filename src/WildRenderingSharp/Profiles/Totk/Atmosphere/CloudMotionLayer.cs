namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>
/// One <c>WorldMgr/PrequelPrCloud/00N.game__wm__PrequelPrCloud.bgyml</c>: what layer N of the cloud dome is made of and how the wind moves it. These
/// override the layer's <c>CloudParamN</c> from <c>master_field.baglclwd</c>.
/// </summary>
public sealed class CloudMotionLayer
{
    public int BaseTextureNo { get; init; }
    public int BaseTextureNoBlend { get; init; } = 2;
    public int NoiseTextureNo { get; init; } = 1;
    public int NoiseTextureNoBlend { get; init; } = 1;
    public CloudWeatherValue BaseTexScale { get; init; } = CloudWeatherValue.Constant(2f);
    public float ScrollSpd { get; init; }
    public float NoiseAdd1 { get; init; }
    public float NoiseAdd1Side { get; init; }
    public float NoiseAdd2 { get; init; }
    public float NoiseAdd2Side { get; init; }
    public bool EnableLimitAltitude { get; init; }
    public float LimitAltitudeMin { get; init; }
    public float LimitAltitudeMax { get; init; }

    public static CloudMotionLayer FromParams(IReadOnlyDictionary<string, object?> map) => new()
    {
        BaseTextureNo = CloudWeatherParams.Int(map, "BaseTextureNo", 0),
        BaseTextureNoBlend = CloudWeatherParams.Int(map, "BaseTextureNo_Blend", 2),
        NoiseTextureNo = CloudWeatherParams.Int(map, "NoiseTextureNo", 1),
        NoiseTextureNoBlend = CloudWeatherParams.Int(map, "NoiseTextureNo_Blend", 1),
        BaseTexScale = CloudWeatherValue.Read(map, "BaseTexScale", 2f),
        ScrollSpd = CloudWeatherParams.Float(map, "ScrollSpd", 0f),
        NoiseAdd1 = CloudWeatherParams.Float(map, "NoiseAdd1", 0f),
        NoiseAdd1Side = CloudWeatherParams.Float(map, "NoiseAdd1_side", 0f),
        NoiseAdd2 = CloudWeatherParams.Float(map, "NoiseAdd2", 0f),
        NoiseAdd2Side = CloudWeatherParams.Float(map, "NoiseAdd2_side", 0f),
        EnableLimitAltitude = CloudWeatherParams.Bool(map, "EnableLimitAltitude", false),
        LimitAltitudeMin = CloudWeatherParams.Float(map, "LimitAltitude_Min", 0f),
        LimitAltitudeMax = CloudWeatherParams.Float(map, "LimitAltitude_Max", 0f),
    };

    /// <summary>1 above <see cref="LimitAltitudeMax"/>, 0 below <see cref="LimitAltitudeMin"/>, linear between; always 1 without a limit.</summary>
    public float AltitudeVisibility(float altitude)
    {
        if (!EnableLimitAltitude)
            return 1f;
        float span = LimitAltitudeMax - LimitAltitudeMin;
        return span <= 0f ? (altitude >= LimitAltitudeMax ? 1f : 0f) : Math.Clamp((altitude - LimitAltitudeMin) / span, 0f, 1f);
    }
}
