using System.Numerics;

namespace WildRenderingSharp.Rendering.Lighting;

/// <summary>The direction toward the sun for an elevation and azimuth.</summary>
internal static class SunDirection
{
    // The direction toward the sun in a Y-up world, with azimuth turning from +X toward -Z as seen from above.
    public static Vector3 FromElevationAzimuth(float elevation, float azimuth)
    {
        float cosElevation = MathF.Cos(elevation);
        return new Vector3(cosElevation * MathF.Cos(azimuth), MathF.Sin(elevation), -cosElevation * MathF.Sin(azimuth));
    }
}
