using System.Numerics;

namespace WildRenderingSharp.Rendering;

public static class SunDirection
{
    /// <summary>The direction toward the sun in a Y-up world, with azimuth turning from +X toward -Z as seen from above.</summary>
    public static Vector3 FromElevationAzimuth(float elevation, float azimuth)
    {
        float cosElevation = MathF.Cos(elevation);
        return new Vector3(cosElevation * MathF.Cos(azimuth), MathF.Sin(elevation), -cosElevation * MathF.Sin(azimuth));
    }

    // A direction, so only the view's rotation applies: its translation would put the camera's position into the sun.
    public static Vector3 ToView(Vector3 sunWorld, Matrix4x4 view) => Vector3.TransformNormal(sunWorld, view);
}
