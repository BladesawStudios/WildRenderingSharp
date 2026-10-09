using System.Numerics;
using WildRenderingSharp.Rendering.Cameras;

namespace WildRenderingSharp.Graphics.Data;

/// <summary>The perspective parameters a shader reads alongside the matrices.</summary>
public readonly record struct Lens(float Aspect, float TanHalfFovY, float Near, float Far)
{
    public Vector2 TanHalf => new(Aspect * TanHalfFovY, TanHalfFovY);
}

/// <summary>A camera's matrices and lens as the games' camera uniform blocks hold them.</summary>
public readonly record struct CameraData(
    Matrix4x4 View, Matrix4x4 ViewProj, Matrix4x4 Proj, Matrix4x4 ViewInv, Lens Lens, Vector2 TexelSize)
{
    public float Aspect => Lens.Aspect;
    public float TanHalfFovY => Lens.TanHalfFovY;
    public float Near => Lens.Near;
    public float Far => Lens.Far;
    public Vector2 TanHalf => Lens.TanHalf;

    public static CameraData From(Camera camera, int width, int height)
    {
        float aspect = width / (float)height;
        var lens = new Lens(aspect, MathF.Tan(camera.FovRadians / 2f), camera.NearPlane, camera.FarPlane);
        var view = camera.ViewMatrix();
        var proj = camera.ProjectionMatrix(aspect);
        return new CameraData(view, view * proj, proj, Inverse(view), lens, Vector2.One / new Vector2(width, height));
    }

    public CameraData ForLight(Matrix4x4 lightView, Matrix4x4 lightProj) =>
        this with { View = lightView, Proj = lightProj, ViewProj = lightView * lightProj, ViewInv = Inverse(lightView) };

    public CameraData FlippedY() => this with { Proj = Proj * ClipSpace.FlipY, ViewProj = ViewProj * ClipSpace.FlipY };

    static Matrix4x4 Inverse(Matrix4x4 m) => Matrix4x4.Invert(m, out var inverse) ? inverse : Matrix4x4.Identity;
}
