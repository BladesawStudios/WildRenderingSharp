using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Tests;

/// <summary>A camera with fixed, asymmetric matrices, so a block that mixes up an axis or a row shows in its bytes.</summary>
static class TestCamera
{
    static readonly Vector4[] View =
        [new(0.8f, 0.1f, -0.2f, 3f), new(-0.1f, 0.9f, 0.3f, -1.5f), new(0.2f, -0.3f, 0.85f, 12f)];

    static readonly Vector4[] Projection =
        [new(1.2f, 0, 0, 0), new(0, 2.1f, 0, 0), new(0, 0, -1.002f, -0.2002f), new(0, 0, -1, 0)];

    static readonly Vector4[] ViewProjection =
        [new(0.96f, 0.12f, -0.24f, 3.6f), new(-0.21f, 1.89f, 0.63f, -3.15f), new(-0.2f, 0.3f, -0.85f, -12.2f), new(-0.2f, 0.3f, -0.85f, -12f)];

    static readonly Vector4[] InverseView =
        [new(0.8f, -0.1f, 0.2f, 1f), new(0.1f, 0.9f, -0.3f, 2f), new(-0.2f, 0.3f, 0.85f, -9f)];

    public static CameraData Default { get; } = new(
        GpuMatrix.FromRows(View), GpuMatrix.FromRows(ViewProjection), GpuMatrix.FromRows(Projection), GpuMatrix.FromRows(InverseView),
        new Lens(16f / 9f, 0.55f, 0.1f, 5000f), new Vector2(1f / 1920, 1f / 1080));

    public static Matrix4x4 ViewProjectionMatrix { get; } = GpuMatrix.FromRows(ViewProjection);

    public static Matrix4x4 InverseViewMatrix { get; } = GpuMatrix.FromRows(InverseView);
}
