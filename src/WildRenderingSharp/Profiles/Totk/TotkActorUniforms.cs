using System.Numerics;
using System.Runtime.InteropServices;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Totk.Ubos;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Profiles.Totk;

static class TotkActorUniforms
{
    public static UniformBlock[] Build(IWorldBasis world, in SkinningData actor)
    {
        Vector4[] placement = world.PlacementRows(actor.PlacementRows);
        var bones = BonePalette(actor.Skeleton, placement, actor.BoneWorld);
        var shape = ShapeMatrixUbo.BuildFromModelMatrix(placement);

        return
        [
            new UniformBlock("bones", (uint)bones.BindingIndex, bones.ToByteArray()),
            new UniformBlock("shpmtx", (uint)shape.BindingIndex, shape.ToByteArray()),
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
            skeleton.InverseModelMatricesAsMatrices(), PlacementAsMatrix(placement));
    }

    /// <summary>Rows with translation in each row's W, as a row-vector matrix - what the palette's skin-then-model multiply needs.</summary>
    static Matrix4x4 PlacementAsMatrix(Vector4[] rows) => new(
        rows[0].X, rows[1].X, rows[2].X, 0,
        rows[0].Y, rows[1].Y, rows[2].Y, 0,
        rows[0].Z, rows[1].Z, rows[2].Z, 0,
        rows[0].W, rows[1].W, rows[2].W, 1);
}
