namespace WildRenderingSharp.Profiles.Totk.Atmosphere.Clouds;

/// <summary>Reads a typed value from a cloud weather file's parameters, with a fallback for a missing one.</summary>
static class CloudWeatherParams
{
    public static float Float(IReadOnlyDictionary<string, object?> map, string key, float fallback) =>
        map.TryGetValue(key, out var v) && v is double d ? (float)d : fallback;

    public static int Int(IReadOnlyDictionary<string, object?> map, string key, int fallback) =>
        map.TryGetValue(key, out var v) && v is double d ? (int)d : fallback;

    public static bool Bool(IReadOnlyDictionary<string, object?> map, string key, bool fallback) =>
        map.TryGetValue(key, out var v) && v is bool b ? b : fallback;
}
