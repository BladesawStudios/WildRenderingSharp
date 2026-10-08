namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>
/// One <c>WorldMgr/PrequelCwCloud/NNN_L.game__wm__PrequelCwCloud.bgyml</c>: how layer L looks under cloud weather NNN (the file number selects the
/// weather, and only layers 0 and 1 have files). It overrides the layer's <c>CloudParamN</c> fields it names.
/// </summary>
public sealed class CloudLookLayer
{
    readonly IReadOnlyDictionary<string, object?> _map;

    public CloudWeatherValue AlphaMul { get; }
    public CloudWeatherValue AlphaThreshold { get; }
    public CloudWeatherValue Density { get; }
    public CloudWeatherValue Distotion { get; }
    public CloudWeatherValue SkyHeight { get; }

    public CloudLookLayer(IReadOnlyDictionary<string, object?> map)
    {
        _map = map;
        AlphaMul = CloudWeatherValue.Read(map, "AlphaMul", 0f);
        AlphaThreshold = CloudWeatherValue.Read(map, "AlphaThreshold", 0f);
        Density = CloudWeatherValue.Read(map, "Density", 0f);
        Distotion = CloudWeatherValue.Read(map, "Distotion", 0f);
        SkyHeight = CloudWeatherValue.Read(map, "SkyHeight", 0f);
    }

    /// <summary>The file's value for <paramref name="key"/>, or <paramref name="current"/> when the file does not name it.</summary>
    public float Override(string key, float current) => CloudWeatherParams.Float(_map, key, current);
}
