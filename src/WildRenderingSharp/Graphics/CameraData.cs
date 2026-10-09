using System.Numerics;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Graphics;

/// <summary>
/// Camera information to be stored in a game shader UBO. UBOs expect matrices
/// in 3x4 format, use Rows() to get that format.
/// </summary>
public readonly record struct CameraData(
    Matrix4x4 View, Matrix4x4 ViewProj, Matrix4x4 Proj, Matrix4x4 ViewInv,
    float Aspect, float TanHalfFovY, float Near, float Far, Vector2 TexelSize)
{
    public Vector2 TanHalf => new(Aspect * TanHalfFovY, TanHalfFovY);

    /// <summary>
    /// Generate camera data from a higher-level camera controller
    /// </summary>
    public static CameraData From(Camera cam, int width, int height) {
        Vector2 invScreenSize = Vector2.One / new Vector2(width, height);
        cam.Aspect = width / (float)height;
        var viewT = cam.view_matrix();
        var projT = cam.proj_matrix();
        var viewProjT = viewT * projT;
        
        Matrix4x4 invViewT = viewT;
        Matrix4x4.Invert(viewT, out invViewT);
        
        return new CameraData(viewT, viewProjT, projT, invViewT, cam.Aspect,
            MathF.Tan(cam.FovRadians / 2f), cam.NearPlane, cam.FarPlane, invScreenSize);
    }

    /// <summary>
    /// Generate camera data from a light (i.e. for rendering from the light's POV)
    /// </summary>
    /// <param name="light">The light projection/view matrices</param>
    /// <param name="cam">The existing camera data</param>
    /// <returns>The new camera data adapted for the light</returns>
    public static CameraData ForLight(ShadowPass.LightMatrices light, CameraData cam) {
        Matrix4x4.Invert(light.View, out var viewInv);
        return new(light.View, light.ViewProj, light.Proj, viewInv,
            Aspect: 1f, TanHalfFovY: 1f, cam.Near, cam.Far, cam.TexelSize);
    }

    static readonly Matrix4x4 FlipY = Matrix4x4.CreateScale(1, -1, 1);

    /// <summary>The same camera with clip-space Y negated, for drawing into targets stored top-down.</summary>
    public CameraData FlippedY() => this with { Proj = Proj * FlipY, ViewProj = ViewProj * FlipY };

    /// Remap [0, 1] depth to [-1, 1] depth
    public static readonly Matrix4x4 ZeroToOneDepthToGl = Matrix4x4.CreateScale(1, 1, 2) * Matrix4x4.CreateTranslation(0, 0, -1);

    /// <summary>
    /// Get a matrix as an array of rows, useful for converting to 3x4 format.
    /// Automatically transposes the matrix.
    /// </summary>
    public static Vector4[] Rows(Matrix4x4 m, int count = 4) {
        var t = Matrix4x4.Transpose(m);
        Vector4[] rows = [
            new(t.M11, t.M12, t.M13, t.M14),
            new(t.M21, t.M22, t.M23, t.M24),
            new(t.M31, t.M32, t.M33, t.M34),
            new(t.M41, t.M42, t.M43, t.M44),
        ];
        return rows[..count];
    }

    
    /// <summary>
    /// Create a matrix from a set of rows and transpose it
    /// </summary>
    public static Matrix4x4 FromRows(ReadOnlySpan<Vector4> rows) {
        var last = rows.Length > 3 ? rows[3] : Vector4.UnitW;
        return Matrix4x4.Transpose(new Matrix4x4(
            rows[0].X, rows[0].Y, rows[0].Z, rows[0].W,
            rows[1].X, rows[1].Y, rows[1].Z, rows[1].W,
            rows[2].X, rows[2].Y, rows[2].Z, rows[2].W,
            last.X, last.Y, last.Z, last.W));
    }
}
