using System.Numerics;

namespace WildRenderingSharp.Rendering;

/// <summary>
/// TotK's in-game item-icon capture: a fixed actor pose, a near-orthographic FOV and a specific sun, palette and exposure, with the
/// camera distance auto-fitted to the loaded model (the Master Sword's fixed distance cuts off anything bigger).
/// </summary>
public static class IconCapturePreset
{
    public static readonly Vector3 ActorRotationDegrees = new(-134.64f, 165.6f, -59.0f);
    public const float FovDegrees = 2.12f;
    public static readonly Vector3 LightDirection = new(-0.562f, 0.694f, 0.451f);
    public const string PaletteName = "IconCapture";
    public const float Exposure = 12.0f;
    public const float AmbientScale = 0.31f;
    public const float MidScale = 7.78f;
    public const float HighlightScale = 1.00f;

    // Pads the exact silhouette fit by a hair so antialiasing at the edge always has headroom - not compensating for looseness
    // in the fit itself (unlike an AABB-based fit, whose tightness depends on how box-shaped the mesh happens to be).
    const float FitMargin = 1.10f;

    public readonly record struct Framing(Vector3 CameraPosition, Vector3 CameraTarget, float Near, float Far, float AoRadius, float ShadowBias);

    public static Vector4[] ActorRotationRows() => EulerRotation.MakeXyzRows3(ActorRotationDegrees.X, ActorRotationDegrees.Y, ActorRotationDegrees.Z);

    public static (float Elevation, float Azimuth) SunElevationAzimuth()
    {
        var towardSun = Vector3.Normalize(LightDirection);
        float elevation = MathF.Asin(Math.Clamp(towardSun.Z, -1f, 1f));
        float azimuth = MathF.Atan2(towardSun.Y, towardSun.X);
        return (elevation, azimuth);
    }

    public static Framing Frame(IReadOnlyList<Vector3> vertices, float aspect = 1f)
    {
        if (vertices.Count == 0)
            throw new ArgumentException("model has no vertices to frame", nameof(vertices));

        var rotRows = ActorRotationRows();
        Vector3 Rotate(Vector3 p) => new(
            rotRows[0].X * p.X + rotRows[0].Y * p.Y + rotRows[0].Z * p.Z,
            rotRows[1].X * p.X + rotRows[1].Y * p.Y + rotRows[1].Z * p.Z,
            rotRows[2].X * p.X + rotRows[2].Y * p.Y + rotRows[2].Z * p.Z);

        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        var rlo = new Vector3(float.MaxValue);
        var rhi = new Vector3(float.MinValue);
        foreach (var v in vertices)
        {
            lo = Vector3.Min(lo, v);
            hi = Vector3.Max(hi, v);
            var r = Rotate(v);
            rlo = Vector3.Min(rlo, r);
            rhi = Vector3.Max(rhi, r);
        }

        var center = (lo + hi) * 0.5f;
        float radius = (hi - lo).Length() * 0.5f + 1e-6f;
        var centerRotated = Rotate(center);

        float halfHeight = (rhi.Y - rlo.Y) * 0.5f;
        float halfWidth = (rhi.X - rlo.X) * 0.5f;
        float tanHalfFov = MathF.Tan(float.DegreesToRadians(FovDegrees) * 0.5f);
        float dist = MathF.Max(halfHeight / tanHalfFov, halfWidth / (aspect * tanHalfFov)) * FitMargin;

        var camPos = new Vector3(centerRotated.X, centerRotated.Y, centerRotated.Z + dist);
        float near = MathF.Max(dist - radius * 3.0f, 0.01f);
        float far = dist + radius * 3.0f;
        var baseFraming = SceneFramingCalculator.ForModelRadius(radius);

        return new Framing(camPos, centerRotated, near, far, baseFraming.AoRadius, baseFraming.ShadowBias);
    }
}
