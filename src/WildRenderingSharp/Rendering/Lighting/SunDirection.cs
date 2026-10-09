using System.Numerics;

namespace WildRenderingSharp.Rendering.Lighting;

public static class SunDirection
{
    /// <summary>The direction toward the sun in a Y-up world, with azimuth turning from +X toward -Z as seen from above.</summary>
    public static Vector3 FromElevationAzimuth(float elevation, float azimuth)
    {
        float cosElevation = MathF.Cos(elevation);
        return new Vector3(cosElevation * MathF.Cos(azimuth), MathF.Sin(elevation), -cosElevation * MathF.Sin(azimuth));
    }
}
