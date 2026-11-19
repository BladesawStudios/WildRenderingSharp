using System.Numerics;
using KingRenderingSharp.Gpu;
using KingRenderingSharp.Content.Graphics.Data;

namespace KingRenderingSharp.Games.Totk.Sky;

/// <summary>The camera's rotation rows, aspect and field of view, as the fullscreen sky passes read them.</summary>
internal readonly record struct SkyView(Vector4[] ViewInvRows, float Aspect, float TanHalfFovY)
{
    public static SkyView From(in CameraData camera) => new(GpuMatrix.Rows(camera.ViewInv, 3), camera.Aspect, camera.TanHalfFovY);
}
