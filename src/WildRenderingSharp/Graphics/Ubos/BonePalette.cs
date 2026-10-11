using System.Numerics;
using System.Runtime.InteropServices;
using WildRenderingSharp.Animation.Posing;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Data;

namespace WildRenderingSharp.Graphics.Ubos;

/// <summary>
/// The engine's skeleton block (<c>g3d_SkeletonUniformBlock</c>, symbol <c>_Mtx</c>), the same in both games: one mat3x4 per bone, in the
/// row-vector convention, so a vertex is <c>v * M</c>.
/// </summary>
internal static class BonePalette
{
    public const int BytesPerBone = 48;
    public const int ByteSize = 0x10000;

    public static UboSpec SpecAt(uint binding) => new("_Mtx", binding, ByteSize);

    // The palette for an actor's pose at bind pose when it has no posed bones, or its placement alone when it has no skeleton.
    public static Ubo For(UboSpec spec, in SkinningData actor)
    {
        if (actor.Skeleton is not { } skeleton)
            return Identity(spec, actor.PlacementRows);

        Matrix4x4[] boneWorld = actor.BoneWorld ?? SkeletonPose.SharedBindPoseWorld(skeleton);
        return Posed(spec, boneWorld, CollectionsMarshal.AsSpan(skeleton.MatrixToBoneList), skeleton.InverseModelMatricesAsMatrices(),
            GpuMatrix.FromRows(actor.PlacementRows));
    }

    public static Ubo Posed(UboSpec spec, ReadOnlySpan<Matrix4x4> boneWorld, ReadOnlySpan<int> matrixToBoneList,
        ReadOnlySpan<Matrix4x4> inverseModelMatrices, Matrix4x4 model)
    {
        int smoothCount = inverseModelMatrices.Length;
        if (smoothCount > matrixToBoneList.Length)
            throw new ArgumentException($"{smoothCount} inverse-bind matrices exceed the {matrixToBoneList.Length}-entry matrixToBoneList they index into", nameof(inverseModelMatrices));
        if (matrixToBoneList.Length * BytesPerBone > spec.ByteSize)
            throw new ArgumentException($"{matrixToBoneList.Length} matrices do not fit a {spec.ByteSize}-byte palette", nameof(matrixToBoneList));

        var block = new UboWriter(spec);
        FillBones(block, GpuMatrix.Rows(Matrix4x4.Identity, 3));
        for (int i = 0; i < matrixToBoneList.Length; i++)
        {
            int bone = matrixToBoneList[i];
            if (bone < 0 || bone >= boneWorld.Length)
                block.Set(BoneField(i), model);
            else
                block.Set(BoneField(i), (i < smoothCount ? inverseModelMatrices[i] * boneWorld[bone] : boneWorld[bone]) * model);
        }
        return block.ToUbo(FrameUniformKeys.Bones);
    }

    // Every bone is the given placement, or the identity when none is given.
    public static Ubo Identity(UboSpec spec, ReadOnlySpan<Vector4> placementRows = default)
    {
        var block = new UboWriter(spec);
        FillBones(block, placementRows.IsEmpty ? GpuMatrix.Rows(Matrix4x4.Identity, 3) : placementRows);
        return block.ToUbo(FrameUniformKeys.Bones);
    }

    static void FillBones(UboWriter block, ReadOnlySpan<Vector4> rows)
    {
        block.Set(BoneField(0), rows);
        block.Repeat(0, BytesPerBone, block.Spec.ByteSize / BytesPerBone);
    }

    static UboMatrix BoneField(int bone) => new(bone * BytesPerBone / 16, 3);
}
