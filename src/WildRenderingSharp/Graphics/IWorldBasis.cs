using System.Numerics;

namespace WildRenderingSharp.Graphics;

/// <summary>
/// Turns what the renderer holds (its own world) into what a game's shaders expect to read.
/// </summary>
public interface IWorldBasis
{
    /// <summary>Rows written for a point in the renderer's world, as rows for a point in the game's.</summary>
    Vector4[] Rows(ReadOnlySpan<Vector4> rows);

    /// <summary>A placement's three rows - model to the renderer's world - as model to the game's.</summary>
    Vector4[] PlacementRows(ReadOnlySpan<Vector4> rows);

    /// <summary>A view-to-world inverse (three rows) for the renderer's world, as one for the game's.</summary>
    Vector4[] InverseRows(ReadOnlySpan<Vector4> viewInv3);

    Vector3 Direction(Vector3 rendererDirection);
}
