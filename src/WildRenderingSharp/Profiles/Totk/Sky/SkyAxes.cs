using System.Numerics;

namespace WildRenderingSharp.Profiles.Totk.Sky;

static class SkyAxes
{
    /// <summary>The renderer's Z-up vector as the Y-up one every agl sky and cloud shader expects.</summary>
    public static Vector3 ToYUp(Vector3 v) => new(v.X, v.Z, v.Y);
}
