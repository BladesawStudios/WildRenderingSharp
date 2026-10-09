using System.Numerics;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Tests;

public sealed class SunDirectionTests
{
    static Camera LookingAlongX(Vector3 eye) => new() { Eye = eye, Target = eye + Vector3.UnitX, Up = Vector3.UnitZ };

    [Fact]
    public void TheSunInViewSpaceDoesNotDependOnWhereTheCameraIs()
    {
        var sun = SunDirection.FromElevationAzimuth(0.6f, 1.1f);

        var atOrigin = SunDirection.ToView(sun, CameraData.From(LookingAlongX(Vector3.Zero), 800, 600).View);
        var farAway = SunDirection.ToView(sun, CameraData.From(LookingAlongX(new Vector3(9000f, -4000f, 1200f)), 800, 600).View);

        Assert.Equal(atOrigin.X, farAway.X, 3);
        Assert.Equal(atOrigin.Y, farAway.Y, 3);
        Assert.Equal(atOrigin.Z, farAway.Z, 3);
    }

    [Fact]
    public void TheSunInViewSpaceStaysAUnitDirection()
    {
        var sun = SunDirection.FromElevationAzimuth(0.3f, -2f);

        var view = SunDirection.ToView(sun, CameraData.From(LookingAlongX(new Vector3(500f, 200f, 80f)), 800, 600).View);

        Assert.Equal(1f, view.Length(), 3);
    }
}
