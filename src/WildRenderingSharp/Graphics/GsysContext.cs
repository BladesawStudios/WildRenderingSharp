namespace WildRenderingSharp.Graphics;

/// <summary>The camera slots of <c>gsys_context</c>, which both games lay out the same way, and the write that fills them from a camera.</summary>
public static class GsysContext
{
    public static readonly UboMatrix View = new(0, 3);
    public static readonly UboMatrix ViewProjection = new(3, 4);
    public static readonly UboMatrix Projection = new(7, 4);
    public static readonly UboMatrix InverseView = new(11, 3);

    public const int NearFar = 14;
    public const int DepthScale = 15;
    public const int FarMinusNear = 16;
    public const int FieldOfView = 17;
    public const int Resolution = 18;

    public static void WriteCamera(UboWriter block, in CameraData camera)
    {
        block.Set(View, camera.View);
        block.Set(ViewProjection, camera.ViewProj);
        block.Set(Projection, camera.Proj);
        block.Set(InverseView, camera.ViewInv);

        float near = camera.Near, far = camera.Far, aspect = camera.Aspect, tanHalfFovY = camera.TanHalfFovY;
        block.Set(NearFar, near, far, near / far, 1f - near / far);
        block.Set(DepthScale, 1f / (far - near), near / (far - near), aspect, 1f / aspect);
        block.Set(FarMinusNear, far - near, 1f / (1f - near / far), 0f, 0f);
        block.Set(FieldOfView, aspect * tanHalfFovY, tanHalfFovY, 2f * MathF.Atan(tanHalfFovY), 0f);
        block.Set(Resolution, 1f / camera.TexelSize.X, 1f / camera.TexelSize.Y, camera.TexelSize.X, camera.TexelSize.Y);
    }
}
