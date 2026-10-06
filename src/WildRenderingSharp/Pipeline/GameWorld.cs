using System.Numerics;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// The game's own world - Y-up - which everything handed to a game program is expressed in.
/// </summary>
/// <remarks>
/// <para>
/// The renderer's world is Z-up (its sky, sun, shadow fitting and hosts' placements are built that
/// way; see <see cref="Hosting.YUpWorld"/>), and for drawing alone that never mattered: a program
/// that multiplies a vertex through the placement, the view and the projection lands it on the same
/// pixel whichever basis the middle of that chain is in. But the game's programs also use world
/// positions directly - water derives its texture coordinates from world X and Z, foliage its wind
/// phase, ground-blended objects their tiling, fog its height - and in the Z-up world "Z" is height.
/// Flat water got a constant V and smeared its normal map into lines.
/// </para>
/// <para>
/// So at the hand-off - <c>ShpMtx</c>, the bone palettes, the instance buffers, every <c>Context</c>,
/// the world-space light direction in <c>Env</c> - the renderer's world is turned the quarter turn
/// back to the game's: a Z-up <c>(x, y, z)</c> is the game's <c>(x, z, -y)</c>.
/// </para>
/// </remarks>
public static class GameWorld
{
    /// <summary>
    /// Rows written for a point in the renderer's world (<c>row · (p, 1)</c> - a view, a
    /// view-projection) re-expressed for a point in the game's.
    /// </summary>
    public static Vector4[] Rows(ReadOnlySpan<Vector4> rows)
    {
        var result = new Vector4[rows.Length];
        for (int i = 0; i < rows.Length; i++)
            result[i] = new Vector4(rows[i].X, rows[i].Z, -rows[i].Y, rows[i].W);
        return result;
    }

    /// <summary>
    /// A placement's three rows - model to the renderer's world - as model to the game's: the
    /// output's rows are reordered, the model side is untouched.
    /// </summary>
    public static Vector4[] PlacementRows(ReadOnlySpan<Vector4> rows) => [rows[0], rows[2], -rows[1]];

    /// <summary>A view-to-world inverse (three rows) for the renderer's world, as one for the game's.</summary>
    public static Vector4[] InverseRows(ReadOnlySpan<Vector4> viewInv3) => PlacementRows(viewInv3);

    /// <summary>A direction in the renderer's world, in the game's.</summary>
    public static Vector3 Direction(Vector3 zUp) => new(zUp.X, zUp.Z, -zUp.Y);
}
