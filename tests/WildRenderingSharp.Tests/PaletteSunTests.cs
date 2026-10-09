using WildRenderingSharp.Profiles.Totk.Atmosphere;

namespace WildRenderingSharp.Tests;

public sealed class PaletteSunTests
{
    [Theory]
    [InlineData("Prequel_MainField_Bluesky_3_Noon", 62f)]
    [InlineData("Prequel_MainField_Cloudy_6_Sunset", 4f)]
    [InlineData("Prequel_MainField_Bluesky_7_Night", -30f)]
    [InlineData("BloodyMoon_DarknessDragon", -30f)]
    public void ThePaletteNameSaysWhereTheSunIs(string palette, float degrees) =>
        Assert.Equal(degrees, PaletteSun.ElevationDegrees(palette));

    [Theory]
    [InlineData("StudioLight")]
    [InlineData("")]
    [InlineData(null)]
    public void APaletteWithoutATimeOfDayLeavesTheSunAlone(string? palette) =>
        Assert.Null(PaletteSun.ElevationDegrees(palette));
}
