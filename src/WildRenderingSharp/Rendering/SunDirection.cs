using System.Numerics;

namespace WildRenderingSharp.Rendering;

public static class SunDirection
{
    public static Vector3 FromElevationAzimuth(float elevation, float azimuth)
    {
        float cosElevation = MathF.Cos(elevation);
        return new Vector3(cosElevation * MathF.Cos(azimuth), cosElevation * MathF.Sin(azimuth), MathF.Sin(elevation));
    }
}
