using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Totk.Ubos;

namespace WildRenderingSharp.Profiles.Totk;

static class TotkCameraUniforms
{
    public static UniformBlock Build(IWorldBasis world, string key, in CameraData camera)
    {
        var context = ContextUbo.BuildForCamera(
            world.Rows(camera.View), world.Rows(camera.ViewProj), camera.Proj, world.InverseRows(camera.ViewInv),
            camera.Aspect, camera.TanHalfFovY, camera.Near, camera.Far, camera.TexelSize);
        return new UniformBlock(key, (uint)context.BindingIndex, context.ToByteArray());
    }
}
