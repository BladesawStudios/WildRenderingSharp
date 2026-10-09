namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>
/// Where the sun is when the game uses a palette. The game picks a palette by time of day (<c>_0_Sunrise</c> to <c>_7_Night</c>) and moves the sun
/// with the clock, so a palette lit by a sun elsewhere never occurs in the game: a night palette under a high sun is blown out, and a noon one
/// under a low sun is orange.
/// </summary>
public static class PaletteSun
{
    static readonly (string Suffix, float Degrees)[] Slots =
    [
        ("Sunrise", 4f), ("Morning1", 18f), ("Morning2", 35f), ("Noon", 62f),
        ("Evening1", 38f), ("Evening2", 18f), ("Sunset", 4f), ("Night", -30f),
    ];

    /// <summary>The sun's height above the horizon in degrees that suits the palette, or null when its name says no time of day.</summary>
    public static float? ElevationDegrees(string? paletteName)
    {
        if (string.IsNullOrEmpty(paletteName))
            return null;
        foreach (var (suffix, degrees) in Slots)
            if (paletteName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return degrees;
        return paletteName.Contains("BloodyMoon", StringComparison.OrdinalIgnoreCase) ? -30f : null;
    }
}
