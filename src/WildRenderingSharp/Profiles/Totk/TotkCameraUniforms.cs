using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Totk.Ubos;

namespace WildRenderingSharp.Profiles.Totk;

static class TotkCameraUniforms
{
    public static UniformBlock Build(IWorldBasis world, string key, in CameraData camera) => UniformBlock.From(key, Context(world, camera));

    /// <summary>The camera block of the field programs, whose frame is a grid of one tile.</summary>
    public static UniformBlock BuildField(IWorldBasis world, in CameraData camera) =>
        UniformBlock.From(TotkUniformKeys.FieldCamera, Context(world, camera).WithTileGrid(1, 1));

    static ContextUbo Context(IWorldBasis world, in CameraData camera) => ContextUbo.BuildForCamera(
        world.Rows(CameraData.Rows(camera.View, 3)), world.Rows(CameraData.Rows(camera.ViewProj)), CameraData.Rows(camera.Proj),
        world.InverseRows(CameraData.Rows(camera.ViewInv, 3)),
        camera.Aspect, camera.TanHalfFovY, camera.Near, camera.Far, camera.TexelSize);
}
