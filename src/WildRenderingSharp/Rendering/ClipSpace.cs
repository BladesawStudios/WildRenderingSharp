using System.Numerics;

namespace WildRenderingSharp.Rendering;

/// <summary>Matrices that convert between System.Numerics clip space and the games'.</summary>
public static class ClipSpace
{
    public static readonly Matrix4x4 ZeroToOneDepthToGl = Matrix4x4.CreateScale(1, 1, 2) * Matrix4x4.CreateTranslation(0, 0, -1);

    public static readonly Matrix4x4 FlipY = Matrix4x4.CreateScale(1, -1, 1);
}
