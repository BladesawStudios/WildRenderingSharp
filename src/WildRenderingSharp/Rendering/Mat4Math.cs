using System.Numerics;

namespace WildRenderingSharp.Rendering;

/// <summary>
/// 4x4 matrix helpers over an array of 4 row vectors, the representation used by the camera and UBO-building code, rather than <see cref="Matrix4x4"/> directly: that type's row-vector convention
/// (translation in <c>M41..M43</c>) does not match the column-vector GL convention these matrices use, and mixing the two invites a transpose bug. <see cref="Matrix4x4"/> is still used internally
/// as a convention-agnostic multiply and invert engine.
/// </summary>
/// </summary>
public static class Mat4Math
{
    public static readonly Vector4[] Identity4 =
    [
        new(1, 0, 0, 0), new(0, 1, 0, 0), new(0, 0, 1, 0), new(0, 0, 0, 1),
    ];

    public static readonly Vector4[] Identity3 =
    [
        new(1, 0, 0, 0), new(0, 1, 0, 0), new(0, 0, 1, 0),
    ];

    /// <summary>Applies the rotation and scale of a 3-row affine matrix to a direction.</summary>
    public static Vector3 TransformDirection(ReadOnlySpan<Vector4> rows, Vector3 d) => new(
        rows[0].X * d.X + rows[0].Y * d.Y + rows[0].Z * d.Z,
        rows[1].X * d.X + rows[1].Y * d.Y + rows[1].Z * d.Z,
        rows[2].X * d.X + rows[2].Y * d.Y + rows[2].Z * d.Z);

    /// <summary>Applies a 3-row affine matrix to a point.</summary>
    public static Vector3 TransformPoint(ReadOnlySpan<Vector4> rows, Vector3 p) => new(
        rows[0].X * p.X + rows[0].Y * p.Y + rows[0].Z * p.Z + rows[0].W,
        rows[1].X * p.X + rows[1].Y * p.Y + rows[1].Z * p.Z + rows[1].W,
        rows[2].X * p.X + rows[2].Y * p.Y + rows[2].Z * p.Z + rows[2].W);

    /// <summary>Appends the implicit (0,0,0,1) fourth row to a 3-row affine matrix.</summary>
    public static Vector4[] ToMat4(ReadOnlySpan<Vector4> affine3Rows)
    {
        if (affine3Rows.Length != 3)
            throw new ArgumentException("expected exactly 3 rows", nameof(affine3Rows));
        return [affine3Rows[0], affine3Rows[1], affine3Rows[2], new Vector4(0, 0, 0, 1)];
    }

    /// <summary>Standard matrix product <c>a * b</c> (row i of the result is a's row i "dotted" through b).</summary>
    public static Vector4[] Multiply(ReadOnlySpan<Vector4> a, ReadOnlySpan<Vector4> b) =>
        FromNumerics(ToNumerics(a) * ToNumerics(b));

    /// <summary>General 4x4 matrix inverse (numeric, convention-agnostic).</summary>
    public static Vector4[] Invert(ReadOnlySpan<Vector4> rows)
    {
        if (!Matrix4x4.Invert(ToNumerics(rows), out var inverted))
            throw new InvalidOperationException("matrix is not invertible");
        return FromNumerics(inverted);
    }

    static Matrix4x4 ToNumerics(ReadOnlySpan<Vector4> rows)
    {
        if (rows.Length != 4)
            throw new ArgumentException("expected exactly 4 rows", nameof(rows));
        return new Matrix4x4(
            rows[0].X, rows[0].Y, rows[0].Z, rows[0].W,
            rows[1].X, rows[1].Y, rows[1].Z, rows[1].W,
            rows[2].X, rows[2].Y, rows[2].Z, rows[2].W,
            rows[3].X, rows[3].Y, rows[3].Z, rows[3].W);
    }

    static Vector4[] FromNumerics(Matrix4x4 m) =>
    [
        new(m.M11, m.M12, m.M13, m.M14),
        new(m.M21, m.M22, m.M23, m.M24),
        new(m.M31, m.M32, m.M33, m.M34),
        new(m.M41, m.M42, m.M43, m.M44),
    ];
}
