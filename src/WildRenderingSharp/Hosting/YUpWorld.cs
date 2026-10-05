using System.Numerics;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Hosting;

/// <summary>
/// Conversions for a host whose world is Y-up - the game's own convention, and most tools'.
/// </summary>
/// <remarks>
/// <para>
/// The renderer's world is Z-up: the sun, sky, clouds, grid and shadow fitting were all built that
/// way, and a model stands up in it through a quarter turn about X (which is why
/// <see cref="Scene.RenderActor.Pitch"/> defaults to 90 degrees). Rather than thread a second
/// convention through every pass, a Y-up host maps its own coordinates across at the edge: a point
/// <c>(x, y, z)</c> becomes <c>(x, -z, y)</c>. That map is a rotation, so lengths, angles and
/// handedness all survive it, and a model placed with a host matrix <c>M</c> is drawn with
/// <c>M * <see cref="ToZUp"/></c>.
/// </para>
/// <para>All matrices here are System.Numerics row-vector matrices (<c>Vector3.Transform(v, m)</c>).</para>
/// </remarks>
public static class YUpWorld
{
    /// <summary>Y-up host space to the renderer's Z-up world: a quarter turn about X.</summary>
    public static readonly Matrix4x4 ToZUp = Matrix4x4.CreateRotationX(MathF.PI / 2f);

    /// <summary>The renderer's Z-up world back to Y-up host space.</summary>
    public static readonly Matrix4x4 FromZUp = Matrix4x4.CreateRotationX(-MathF.PI / 2f);

    public static Vector3 Point(Vector3 yUp) => new(yUp.X, -yUp.Z, yUp.Y);

    public static Vector3 PointBack(Vector3 zUp) => new(zUp.X, zUp.Z, -zUp.Y);

    /// <summary>
    /// A model placement for the renderer from the host's own Y-up model matrix - what
    /// <see cref="Scene.RenderActor.TransformOverride"/> takes. Identity puts a BFRES model exactly
    /// where it was authored, standing upright.
    /// </summary>
    public static Vector4[] ActorRows(Matrix4x4 hostModelMatrix) =>
        Scene.RenderActor.RowsFromMatrix(hostModelMatrix * ToZUp);

    /// <summary>A renderer camera looking from <paramref name="eye"/> at <paramref name="target"/>, both in Y-up host space.</summary>
    public static Camera Camera(Vector3 eye, Vector3 target, float fovDegrees, float nearPlane, float farPlane, Vector3? up = null) => new()
    {
        Eye = Point(eye),
        Target = Point(target),
        Up = Point(up ?? Vector3.UnitY),
        FovDegrees = fovDegrees,
        NearPlane = nearPlane,
        FarPlane = farPlane,
    };

    /// <summary>
    /// Sun elevation/azimuth (the renderer's <see cref="LightingContext.SunElevation"/>/<see cref="LightingContext.SunAzimuth"/>)
    /// for a direction TOWARD the sun given in Y-up host space.
    /// </summary>
    public static (float Elevation, float Azimuth) SunAngles(Vector3 towardSunYUp)
    {
        var d = Vector3.Normalize(Point(towardSunYUp));
        return (MathF.Asin(Math.Clamp(d.Z, -1f, 1f)), MathF.Atan2(d.Y, d.X));
    }
}
