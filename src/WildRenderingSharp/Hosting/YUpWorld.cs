using System.Numerics;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Hosting;

/// <summary>Conversions for a host whose world is Y-up - the game's own convention, and most tools'.</summary>
public static class YUpWorld
{
    public static readonly Matrix4x4 ToZUp = Matrix4x4.CreateRotationX(MathF.PI / 2f);

    public static readonly Matrix4x4 FromZUp = Matrix4x4.CreateRotationX(-MathF.PI / 2f);

    // Convert a point from Y up to Z up
    public static Vector3 Point(Vector3 yUp) => new(yUp.X, -yUp.Z, yUp.Y);

    // Convert a point from Z up to Y up
    public static Vector3 PointBack(Vector3 zUp) => new(zUp.X, zUp.Z, -zUp.Y);

    public static Vector4[] ActorRows(Matrix4x4 hostModelMatrix) =>
        CameraData.Rows(hostModelMatrix * ToZUp, 3);

    public static Camera Camera(Vector3 eye, Vector3 target, float fovDegrees, float nearPlane, float farPlane, Vector3? up = null)
    {
        var c = new Camera();
        // The renderer works in Z-up, like the actors (ActorRows) and the sun.
        c.Eye = Point(eye);
        c.Target = Point(target);
        c.Up = Point(up ?? Vector3.UnitY);
        c.FovDegrees = fovDegrees;
        c.NearPlane = nearPlane;
        c.FarPlane = farPlane;

        return c;
    }
}
