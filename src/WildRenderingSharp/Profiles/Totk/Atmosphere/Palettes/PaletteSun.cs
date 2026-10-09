namespace WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;

/// <summary>Where the sun is when the game uses a palette. The game moves the sun with the time of day that picks the palette, so a palette is never lit from a different sun.</summary>
internal static class PaletteSun
{
    static readonly (string Suffix, float Degrees)[] Slots =
    [
        ("Sunrise", 4f), ("Morning1", 18f), ("Morning2", 35f), ("Noon", 62f),
        ("Evening1", 38f), ("Evening2", 18f), ("Sunset", 4f), ("Night", -30f),
    ];

    // The sun's height above the horizon in degrees that suits the palette, or null when its name says no time of day.
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
