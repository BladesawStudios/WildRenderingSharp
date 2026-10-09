namespace WildRenderingSharp.Profiles.Totk.Atmosphere.Clouds;

/// <summary>
/// A weather cloud parameter that either holds <see cref="Base"/> or breathes between <see cref="Min"/> and <see cref="Max"/> on a sine whose
/// phase advances by <see cref="SinSeedAdd"/> per frame (<c>FUN_7100b4bc00</c> in the game: <c>(sin(phase) + 1) / 2</c> between the two).
/// </summary>
public readonly record struct CloudWeatherValue(float Base, float Min, float Max, bool UseBase, float SinSeedAdd)
{
    /// <summary>The game runs at 30 frames a second; the per-frame phase step is converted to seconds with it.</summary>
    const float FramesPerSecond = 30f;

    public static CloudWeatherValue Constant(float value) => new(value, value, value, true, 0f);

    public float At(double seconds)
    {
        if (UseBase)
            return Base;
        float s = 0.5f * (1f + MathF.Sin(SinSeedAdd * FramesPerSecond * (float)seconds));
        return Min + (Max - Min) * s;
    }

    /// <summary>
    /// Reads <c>name</c>, <c>nameMin</c>, <c>nameMax</c>, <c>nameSinSeedAdd</c> and <c>name_IsUseBase</c>. The flag is false when absent, as in the
    /// shipped files, so a value with no Min and Max can only hold its base.
    /// </summary>
    public static CloudWeatherValue Read(IReadOnlyDictionary<string, object?> map, string name, float fallback)
    {
        float baseValue = CloudWeatherParams.Float(map, name, fallback);
        if (!map.ContainsKey(name + "Min") || !map.ContainsKey(name + "Max"))
            return Constant(baseValue);

        return new CloudWeatherValue(
            baseValue,
            CloudWeatherParams.Float(map, name + "Min", baseValue),
            CloudWeatherParams.Float(map, name + "Max", baseValue),
            CloudWeatherParams.Bool(map, name + "_IsUseBase", false),
            CloudWeatherParams.Float(map, name + "SinSeedAdd", 0f));
    }
}
