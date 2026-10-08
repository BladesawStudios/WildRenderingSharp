using System.Numerics;
using WildRenderingSharp.Profiles.Totk.Atmosphere;

namespace WildRenderingSharp.Tests;

public sealed class CloudWeatherTests
{
    static IReadOnlyDictionary<string, object?> Map(params (string Key, object Value)[] entries) =>
        entries.ToDictionary(e => e.Key, e => (object?)e.Value);

    [Fact]
    public void ValueWithoutMinAndMaxHoldsItsBase()
    {
        var value = CloudWeatherValue.Read(Map(("Density", 0.35)), "Density", 0f);
        Assert.Equal(0.35f, value.At(0), 5);
        Assert.Equal(0.35f, value.At(1234), 5);
    }

    [Fact]
    public void BreathingValueStartsAtTheMidpointAndStaysInRange()
    {
        var map = Map(("Density", 0.35), ("DensityMin", 0.2), ("DensityMax", 0.4), ("DensitySinSeedAdd", 0.01));
        var value = CloudWeatherValue.Read(map, "Density", 0f);

        Assert.Equal(0.3f, value.At(0), 5);
        for (double t = 0; t < 120; t += 0.5)
            Assert.InRange(value.At(t), 0.2f - 1e-5f, 0.4f + 1e-5f);
    }

    [Fact]
    public void UseBaseFlagPinsABreathingValue()
    {
        var map = Map(("Density", 0.35), ("DensityMin", 0.2), ("DensityMax", 0.4), ("Density_IsUseBase", true));
        Assert.Equal(0.35f, CloudWeatherValue.Read(map, "Density", 0f).At(17), 5);
    }

    [Fact]
    public void AltitudeLimitFadesTheLayerIn()
    {
        var motion = CloudMotionLayer.FromParams(Map(("EnableLimitAltitude", true), ("LimitAltitude_Min", 900.0), ("LimitAltitude_Max", 950.0)));
        Assert.Equal(0f, motion.AltitudeVisibility(0f));
        Assert.Equal(0.5f, motion.AltitudeVisibility(925f), 5);
        Assert.Equal(1f, motion.AltitudeVisibility(3000f));
    }

    [Fact]
    public void LayerWithoutALookIsNotDrawn()
    {
        Assert.Null(CloudLayerResolver.Resolve(CloudPostFxLayer.Default, null, null, CloudLayerResolver.CapturedWind, 0, 0));
    }

    [Fact]
    public void LayerBelowItsAltitudeLimitHasNoAlpha()
    {
        var motion = CloudMotionLayer.FromParams(Map(("EnableLimitAltitude", true), ("LimitAltitude_Min", 900.0), ("LimitAltitude_Max", 950.0)));
        var look = new CloudLookLayer(Map(("AlphaMul", 0.9), ("Density", 0.5)));

        Assert.Null(CloudLayerResolver.Resolve(CloudPostFxLayer.Default, motion, look, CloudLayerResolver.CapturedWind, 0, 0));
        Assert.NotNull(CloudLayerResolver.Resolve(CloudPostFxLayer.Default, motion, look, CloudLayerResolver.CapturedWind, 0, 1000));
    }

    [Fact]
    public void WindMovesTheNoiseTheWayTheGameCombinesAlongAndSideTerms()
    {
        var motion = CloudMotionLayer.FromParams(Map(("NoiseAdd1", -1.0), ("NoiseAdd1_side", -3.0), ("NoiseAdd2", -6.0), ("NoiseAdd2_side", 3.0)));
        var look = new CloudLookLayer(Map(("AlphaMul", 0.9)));
        var wind = new Vector2(0.442f, -0.897f);

        var layer = CloudLayerResolver.Resolve(CloudPostFxLayer.Default, motion, look, wind, 0, 0)!;

        // The ratios the capture's four noise offsets have (0.4358, -0.0825, -1.0337, 1.2987) are the ratios of these speeds.
        AssertRatio(0.4358f / -0.0825f, layer.NoiseSpeed1X / layer.NoiseSpeed1Y);
        AssertRatio(-1.0337f / -0.0825f, layer.NoiseSpeed2X / layer.NoiseSpeed1Y);
        AssertRatio(1.2987f / -0.0825f, layer.NoiseSpeed2Y / layer.NoiseSpeed1Y);
    }

    // The wind direction is fitted to three digits, so the ratios agree to a few percent.
    static void AssertRatio(float expected, float actual) => Assert.InRange(actual / expected, 0.96f, 1.04f);
}
