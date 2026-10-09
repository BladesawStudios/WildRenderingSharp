using System.Numerics;

namespace WildRenderingSharp.Rendering;

/// <summary>Matrices that convert between the conventions of System.Numerics and the games' clip space.</summary>
public static class ClipSpace
{
    /// <summary>Remaps [0, 1] depth to [-1, 1] depth.</summary>
    public static readonly Matrix4x4 ZeroToOneDepthToGl = Matrix4x4.CreateScale(1, 1, 2) * Matrix4x4.CreateTranslation(0, 0, -1);

    /// <summary>Negates clip-space Y, for drawing into targets stored top-down.</summary>
    public static readonly Matrix4x4 FlipY = Matrix4x4.CreateScale(1, -1, 1);
}
