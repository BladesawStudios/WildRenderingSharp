using System.Numerics;
using System.Runtime.InteropServices;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Graphics;

/// <summary>
/// The engine's skeleton block (<c>g3d_SkeletonUniformBlock</c>, symbol <c>_Mtx</c>), the same in both games. Each bone is a mat3x4 of three
/// vec4 rows in the row-vector convention, so a vertex is <c>v * M</c>.
/// </summary>
public sealed class BonePaletteUbo : IUboBlock
{
    public const int BytesPerBone = 48;
    public const int DefaultByteSize = 65536;

    readonly byte[] _data;
    readonly uint _binding;

    public string Name => "_Mtx";
    public int BindingIndex => (int)_binding;

    BonePaletteUbo(byte[] data, uint binding)
    {
        _data = data;
        _binding = binding;
    }

    /// <summary>The palette for an actor's placement and pose, at bind pose when it has no posed bones, or identity when it has no skeleton.</summary>
    public static BonePaletteUbo For(in SkinningData actor, uint binding)
    {
        if (actor.Skeleton is not { } skeleton)
            return FillIdentity(binding, actor.PlacementRows);

        Matrix4x4[] boneWorld = actor.BoneWorld ?? SkeletonPose.BindPoseWorldMatrices(skeleton);
        return Build(boneWorld, CollectionsMarshal.AsSpan(skeleton.MatrixToBoneList), skeleton.InverseModelMatricesAsMatrices(),
            binding, GpuMatrix.FromRows(actor.PlacementRows));
    }

    public static BonePaletteUbo Build(
        ReadOnlySpan<Matrix4x4> boneWorld,
        ReadOnlySpan<int> matrixToBoneList,
        ReadOnlySpan<Matrix4x4> inverseModelMatrices,
        uint binding,
        Matrix4x4? modelTransform = null,
        int byteSize = DefaultByteSize)
    {
        int smoothCount = inverseModelMatrices.Length;
        if (smoothCount > matrixToBoneList.Length)
            throw new ArgumentException(
                $"{smoothCount} inverse-bind matrices exceed the {matrixToBoneList.Length}-entry matrixToBoneList they index into",
                nameof(inverseModelMatrices));

        int totalCount = matrixToBoneList.Length;
        if (totalCount * BytesPerBone > byteSize)
            throw new ArgumentException($"{totalCount} matrices do not fit a {byteSize}-byte palette", nameof(byteSize));

        Matrix4x4 model = modelTransform ?? Matrix4x4.Identity;
        byte[] data = FillIdentity(binding, byteSize: byteSize).ToByteArray();

        for (int i = 0; i < smoothCount; i++)
        {
            int bone = matrixToBoneList[i];
            WriteMatrix(data, i, bone >= 0 && bone < boneWorld.Length ? inverseModelMatrices[i] * boneWorld[bone] * model : model);
        }
        for (int j = smoothCount; j < totalCount; j++)
        {
            int bone = matrixToBoneList[j];
            WriteMatrix(data, j, bone >= 0 && bone < boneWorld.Length ? boneWorld[bone] * model : model);
        }
        return new BonePaletteUbo(data, binding);
    }

    public static BonePaletteUbo FillIdentity(uint binding, ReadOnlySpan<Vector4> modelRows = default, int byteSize = DefaultByteSize)
    {
        if (!modelRows.IsEmpty && modelRows.Length != 3)
            throw new ArgumentException("modelRows must have exactly 3 rows (mat3x4)", nameof(modelRows));

        var bone = new Std140Block(BytesPerBone);
        bone.WriteRows(0, modelRows.IsEmpty ? GpuMatrix.Rows(Matrix4x4.Identity, 3) : modelRows);
        byte[] oneBone = bone.ToByteArray();

        var data = new byte[byteSize];
        for (int offset = 0; offset + BytesPerBone <= byteSize; offset += BytesPerBone)
            oneBone.CopyTo(data, offset);
        return new BonePaletteUbo(data, binding);
    }

    // The rows are the transpose of the matrix: translation sits in the W of each row, which is column 4 of a row-vector matrix.
    static void WriteMatrix(byte[] data, int slot, in Matrix4x4 matrix)
    {
        var block = new Std140Block(BytesPerBone);
        block.WriteRows(0, GpuMatrix.Rows(matrix, 3));
        block.ToByteArray().CopyTo(data, slot * BytesPerBone);
    }

    public byte[] ToByteArray() => (byte[])_data.Clone();
}
