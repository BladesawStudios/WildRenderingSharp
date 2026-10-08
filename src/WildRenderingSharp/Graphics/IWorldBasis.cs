using System.Numerics;

namespace WildRenderingSharp.Graphics;

/// <summary>Turns what the renderer holds (its own world) into what a game's shaders expect to read.</summary>
public interface IWorldBasis
{
    Vector4[] Rows(ReadOnlySpan<Vector4> rows);

    Vector4[] PlacementRows(ReadOnlySpan<Vector4> rows);

    Vector4[] InverseRows(ReadOnlySpan<Vector4> viewInv3);

    Vector3 Direction(Vector3 rendererDirection);
}
