using System.Numerics;
using System.Runtime.InteropServices;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Totk.Ubos;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Profiles.Totk;

static class TotkActorUniforms
{
    public static UniformBlock[] Build(in SkinningData actor)
    {
        Vector4[] placement = actor.PlacementRows;
        var bones = BonePalette(actor.Skeleton, placement, actor.BoneWorld);
        var shape = ShapeMatrixUbo.BuildFromModelMatrix(placement);

        return
        [
            UniformBlock.From(FrameUniformKeys.Bones, bones),
            UniformBlock.From(FrameUniformKeys.ShapeMatrix, shape),
        ];
    }

    public static UniformBlock[] InstancedPlaceholders { get; } =
    [
        UniformBlock.Zeroed(BonePaletteUbo.Binding),
        UniformBlock.Zeroed(ShapeMatrixUbo.Binding),
    ];

    static BonePaletteUbo BonePalette(SkeletonManifest? skeleton, Vector4[] placement, Matrix4x4[]? boneWorldOverride)
    {
        if (skeleton is null)
            return BonePaletteUbo.FillIdentity(placement);

        Matrix4x4[] boneWorld = boneWorldOverride ?? SkeletonPose.BindPoseWorldMatrices(skeleton);
        return BonePaletteUbo.Build(boneWorld, CollectionsMarshal.AsSpan(skeleton.MatrixToBoneList),
            skeleton.InverseModelMatricesAsMatrices(), CameraData.FromRows(placement));
    }
}
