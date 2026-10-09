using System.Numerics;
using System.Runtime.InteropServices;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Botw.Ubos;
using WildRenderingSharp.Profiles.Totk.Ubos;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Profiles.Botw;

static class BotwUniforms
{
    public static UniformBlock Camera(IWorldBasis world, string key, in CameraData camera) =>
        UniformBlock.From(key, BotwContextUbo.ForCamera(
            world.Rows(camera.View), world.Rows(camera.ViewProj), camera.Proj, world.InverseRows(camera.ViewInv),
            camera.Aspect, camera.TanHalfFovY, camera.Near, camera.Far, camera.TexelSize));

    public static IReadOnlyList<UniformBlock> Actor(IWorldBasis world, in SkinningData actor)
    {
        Vector4[] placement = world.PlacementRows(actor.PlacementRows);
        var skeleton = actor.Skeleton;
        BonePaletteUbo bones = skeleton is null
            ? BonePaletteUbo.FillIdentity(placement)
            : BonePaletteUbo.Build(actor.BoneWorld ?? SkeletonPose.BindPoseWorldMatrices(skeleton),
                CollectionsMarshal.AsSpan(skeleton.MatrixToBoneList), skeleton.InverseModelMatricesAsMatrices(), PlacementMatrix(placement));
        return [UniformBlock.From(FrameUniformKeys.Bones, bones)];
    }

    public static IReadOnlyList<UniformBlock> Placeholders { get; } = [UniformBlock.Zeroed(BotwBindings.Bones)];

    static Matrix4x4 PlacementMatrix(Vector4[] rows) => new(
        rows[0].X, rows[1].X, rows[2].X, 0,
        rows[0].Y, rows[1].Y, rows[2].Y, 0,
        rows[0].Z, rows[1].Z, rows[2].Z, 0,
        rows[0].W, rows[1].W, rows[2].W, 1);
}
