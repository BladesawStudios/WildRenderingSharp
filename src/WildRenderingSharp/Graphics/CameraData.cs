using System.Numerics;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Graphics;

/// <summary>
/// A viewpoint in the renderer's world, as the row arrays the rest of the renderer uses (see <see cref="Mat4Math"/>): view and
/// inverse view have three rows, the projections four.
/// </summary>
public readonly record struct CameraData(
    Vector4[] View, Vector4[] ViewProj, Vector4[] Proj, Vector4[] ViewInv,
    float Aspect, float TanHalfFovY, float Near, float Far, Vector2 TexelSize)
{
    public Vector2 TanHalf => new(Aspect * TanHalfFovY, TanHalfFovY);

    public static CameraData From(Camera camera, int width, int height)
    {
        var vp = camera.BuildViewProjection(width, height);
        var view4 = Mat4Math.ToMat4(vp.View);
        return new CameraData(vp.View, Mat4Math.Multiply(vp.Proj, view4), vp.Proj, Mat4Math.Invert(view4)[..3],
            vp.Aspect, vp.TanHalfFovY, camera.NearPlane, camera.FarPlane, new Vector2(1f / width, 1f / height));
    }

    /// <summary>The light's point of view, sharing the depth range and texel size of <paramref name="scene"/>.</summary>
    public static CameraData ForLight(ShadowPass.LightMatrices light, CameraData scene) => new(
        light.View3Rows, light.ViewProj, light.Proj, Mat4Math.Invert(Mat4Math.ToMat4(light.View3Rows))[..3],
        Aspect: 1f, TanHalfFovY: 1f, scene.Near, scene.Far, scene.TexelSize);

    /// <summary>The same view with the projection's second row negated - the upper-left window origin the games' shaders were written for.</summary>
    public CameraData FlippedY() => this with { Proj = FlipRow1(Proj), ViewProj = FlipRow1(ViewProj) };

    static Vector4[] FlipRow1(Vector4[] rows) => [rows[0], -rows[1], rows[2], rows[3]];
}
