using System.Numerics;

namespace WildRenderingSharp.Graphics;

/// <summary>
/// The Y-up world the Wild games work in, reached from the renderer's Z-up one by a quarter turn: a Z-up <c>(x, y, z)</c> is <c>(x,
/// z, -y)</c> here.
/// </summary>
/// <remarks>
/// Shaders use world positions directly (water derives its texture coordinates from X and Z,
/// foliage its wind phase from them), so a basis that is merely self-consistent is not enough.
/// </remarks>
public sealed class YUpWorldBasis : IWorldBasis
{
    public static readonly YUpWorldBasis Instance = new();

    public Vector4[] Rows(ReadOnlySpan<Vector4> rows)
    {
        var result = new Vector4[rows.Length];
        for (int i = 0; i < rows.Length; i++)
            result[i] = new Vector4(rows[i].X, rows[i].Z, -rows[i].Y, rows[i].W);
        return result;
    }

    public Vector4[] PlacementRows(ReadOnlySpan<Vector4> rows) => [rows[0], rows[2], -rows[1]];

    public Vector4[] InverseRows(ReadOnlySpan<Vector4> viewInv3) => PlacementRows(viewInv3);

    public Vector3 Direction(Vector3 rendererDirection) => new(rendererDirection.X, rendererDirection.Z, -rendererDirection.Y);
}
