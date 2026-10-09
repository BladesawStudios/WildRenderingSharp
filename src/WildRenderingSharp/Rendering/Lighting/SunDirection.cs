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

    // A direction, so only the view's rotation applies: its translation would put the camera's position into the sun.
    public static Vector3 ToView(Vector3 sunWorld, Matrix4x4 view) => Vector3.TransformNormal(sunWorld, view);
}
