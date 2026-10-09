using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Totk.Ubos;

namespace WildRenderingSharp.Profiles.Totk;

static class TotkCameraUniforms
{
    public static UniformBlock Build(string key, in CameraData camera) => UniformBlock.From(key, Context(camera));

    /// <summary>The camera block of the field programs, whose frame is a grid of one tile.</summary>
    public static UniformBlock BuildField(in CameraData camera) =>
        UniformBlock.From(TotkUniformKeys.FieldCamera, Context(camera).WithTileGrid(1, 1));

    static ContextUbo Context(in CameraData camera) => ContextUbo.BuildForCamera(
        CameraData.Rows(camera.View, 3), CameraData.Rows(camera.ViewProj), CameraData.Rows(camera.Proj),
        CameraData.Rows(camera.ViewInv, 3),
        camera.Aspect, camera.TanHalfFovY, camera.Near, camera.Far, camera.TexelSize);
}
