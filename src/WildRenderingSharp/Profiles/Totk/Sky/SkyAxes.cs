using System.Numerics;

namespace WildRenderingSharp.Profiles.Totk.Sky;

static class SkyAxes
{
    public static Vector3 ToYUp(Vector3 v) => new(v.X, v.Z, v.Y);
}
