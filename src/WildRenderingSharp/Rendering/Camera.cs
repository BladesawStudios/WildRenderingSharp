using System.Numerics;

namespace WildRenderingSharp.Rendering;

/// <summary>A perspective viewpoint; the aspect ratio comes from the target it renders into.</summary>
public sealed class Camera
{
    public Vector3 Eye { get; set; } = new(0, 0, 3);
    public Vector3 Target { get; set; } = Vector3.Zero;
    public Vector3 Up { get; set; } = Vector3.UnitY;

    public float FovRadians { get; set; } = float.DegreesToRadians(70f);
    public float NearPlane { get; set; } = 0.1f;
    public float FarPlane { get; set; } = 10000f;

    public float FovDegrees
    {
        get => float.RadiansToDegrees(FovRadians);
        set => FovRadians = float.DegreesToRadians(value);
    }

    public Matrix4x4 ViewMatrix() => Matrix4x4.CreateLookAt(Eye, Target, Up);

    public Matrix4x4 ProjectionMatrix(float aspect) =>
        Matrix4x4.CreatePerspectiveFieldOfView(FovRadians, aspect, NearPlane, FarPlane) * ClipSpace.ZeroToOneDepthToGl;
}
