using System.Numerics;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Hosting;

/// <summary>Conversions for a host whose world is Y-up - the game's own convention, and most tools'.</summary>
public static class YUpWorld
{
    public static readonly Matrix4x4 ToZUp = Matrix4x4.CreateRotationX(MathF.PI / 2f);

    public static readonly Matrix4x4 FromZUp = Matrix4x4.CreateRotationX(-MathF.PI / 2f);

    public static Vector3 Point(Vector3 yUp) => new(yUp.X, -yUp.Z, yUp.Y);

    public static Vector3 PointBack(Vector3 zUp) => new(zUp.X, zUp.Z, -zUp.Y);

    public static Vector4[] ActorRows(Matrix4x4 hostModelMatrix) =>
        Scene.RenderActor.RowsFromMatrix(hostModelMatrix * ToZUp);

    public static Camera Camera(Vector3 eye, Vector3 target, float fovDegrees, float nearPlane, float farPlane, Vector3? up = null) => new()
    {
        Eye = Point(eye),
        Target = Point(target),
        Up = Point(up ?? Vector3.UnitY),
        FovDegrees = fovDegrees,
        NearPlane = nearPlane,
        FarPlane = farPlane,
    };

    public static (float Elevation, float Azimuth) SunAngles(Vector3 towardSunYUp)
    {
        var d = Vector3.Normalize(Point(towardSunYUp));
        return (MathF.Asin(Math.Clamp(d.Z, -1f, 1f)), MathF.Atan2(d.Y, d.X));
    }
}
