using System.Numerics;

namespace WildRenderingSharp.Rendering;

/// <summary>
/// A GL-convention (right-handed, column-vector) look-at camera. The view and projection matrices are returned as row arrays (see <see cref="Mat4Math"/>), not <see cref="Matrix4x4"/>,
/// to keep the convention the rest of the pipeline expects.
/// </summary>
public class Camera
{
    public float FovDegrees { get; set; } = 38.0f;
    public float NearPlane { get; set; } = 0.05f;
    public float FarPlane { get; set; } = 4000.0f;

    public Vector3 Eye { get; set; } = new(0, 0, 3);
    public Vector3 Target { get; set; } = Vector3.Zero;

    /// <summary>TotK/BFRES models are Z-up; the icon-capture preset uses the in-game Y-up convention instead.</summary>
    public Vector3 Up { get; set; } = new(0, 0, 1);

    public readonly record struct ViewProjection(Vector4[] View, Vector4[] Proj, float Aspect, float TanHalfFovY);

    public ViewProjection BuildViewProjection(int width, int height)
    {
        var f = Vector3.Normalize(Target - Eye);
        var s = Vector3.Normalize(Vector3.Cross(f, Up));
        var u = Vector3.Cross(s, f);

        Vector4[] view =
        [
            new(s.X, s.Y, s.Z, -Vector3.Dot(s, Eye)),
            new(u.X, u.Y, u.Z, -Vector3.Dot(u, Eye)),
            new(-f.X, -f.Y, -f.Z, Vector3.Dot(f, Eye)),
        ];

        float aspect = width / (float)height;
        float tanHalfFovY = MathF.Tan(float.DegreesToRadians(FovDegrees) * 0.5f);
        float near = NearPlane, far = FarPlane;

        Vector4[] proj =
        [
            new(1f / (aspect * tanHalfFovY), 0, 0, 0),
            new(0, 1f / tanHalfFovY, 0, 0),
            new(0, 0, -(far + near) / (far - near), -(2f * far * near) / (far - near)),
            new(0, 0, -1, 0),
        ];

        return new ViewProjection(view, proj, aspect, tanHalfFovY);
    }
}
