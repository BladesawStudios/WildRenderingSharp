using System.Numerics;

namespace WildRenderingSharp.Graphics;

/// <summary>Turns what the renderer holds (its own world) into what a game's shaders expect to read.</summary>
public interface IWorldBasis
{
    Vector4[] Rows(ReadOnlySpan<Vector4> rows);

    /// <summary><paramref name="rendererTransform"/> made to take points in the game's world instead of the renderer's; the matrix form of <see cref="Rows"/>.</summary>
    Matrix4x4 FromGameWorld(Matrix4x4 rendererTransform);

    Vector4[] PlacementRows(ReadOnlySpan<Vector4> rows);

    Vector4[] InverseRows(ReadOnlySpan<Vector4> viewInv3);

    Vector3 Direction(Vector3 rendererDirection);
}
