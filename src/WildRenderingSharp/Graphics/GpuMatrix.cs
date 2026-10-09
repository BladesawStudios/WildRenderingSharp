using System.Numerics;

namespace WildRenderingSharp.Graphics;

/// <summary>Converts between System.Numerics matrices and the transposed row arrays the games' uniform blocks hold.</summary>
public static class GpuMatrix
{
    /// <param name="count">3 for the 3x4 matrices the games store, 4 for a full matrix.</param>
    public static Vector4[] Rows(Matrix4x4 m, int count = 4)
    {
        var t = Matrix4x4.Transpose(m);
        Vector4[] rows =
        [
            new(t.M11, t.M12, t.M13, t.M14),
            new(t.M21, t.M22, t.M23, t.M24),
            new(t.M31, t.M32, t.M33, t.M34),
            new(t.M41, t.M42, t.M43, t.M44),
        ];
        return rows[..count];
    }

    /// <summary>The inverse of <see cref="Rows"/>; a missing fourth row is (0, 0, 0, 1).</summary>
    public static Matrix4x4 FromRows(ReadOnlySpan<Vector4> rows)
    {
        var last = rows.Length > 3 ? rows[3] : Vector4.UnitW;
        return Matrix4x4.Transpose(new Matrix4x4(
            rows[0].X, rows[0].Y, rows[0].Z, rows[0].W,
            rows[1].X, rows[1].Y, rows[1].Z, rows[1].W,
            rows[2].X, rows[2].Y, rows[2].Z, rows[2].W,
            last.X, last.Y, last.Z, last.W));
    }
}
