using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>
/// TotK's skinning matrix palette (the engine's <c>g3d_SkeletonUniformBlock</c>, the shader symbol <c>_Mtx</c>), binding 2. 48
/// bytes per matrix: a mat3x4 of three vec4 rows, row-vector convention (a vertex is <c>v * M</c>; composition is "apply the first
/// operand, then the second"; see <c>SkeletonPose</c>).
/// </summary>
public sealed class BonePaletteUbo : IUboBlock
{
    public const int BytesPerBone = 48;
    public const int DefaultByteSize = 65536;

    readonly byte[] _data;

    public string Name => "_Mtx";
    public const uint Binding = TotkBindings.Bones;
    public int BindingIndex => (int)Binding;

    BonePaletteUbo(byte[] data) => _data = data;

    public static BonePaletteUbo Build(
        ReadOnlySpan<Matrix4x4> boneWorld,
        ReadOnlySpan<int> matrixToBoneList,
        ReadOnlySpan<Matrix4x4> inverseModelMatrices,
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
        byte[] data = FillIdentity(byteSize: byteSize).ToByteArray();

        for (int i = 0; i < smoothCount; i++)
        {
            int bone = matrixToBoneList[i];
            Matrix4x4 skin = bone >= 0 && bone < boneWorld.Length
                ? inverseModelMatrices[i] * boneWorld[bone] * model
                : model;
            WriteMatrix(data, i, skin);
        }
        for (int j = smoothCount; j < totalCount; j++)
        {
            int bone = matrixToBoneList[j];
            Matrix4x4 rigid = bone >= 0 && bone < boneWorld.Length ? boneWorld[bone] * model : model;
            WriteMatrix(data, j, rigid);
        }

        return new BonePaletteUbo(data);
    }

    // m is a Matrix4x4 composed the native .NET way: row-vector convention, translation in row 4 (M41-M43). The GPU "rows"
    // convention used everywhere else here (CameraData.Rows, EulerRotation, Std140Block.WriteRows) packs translation into the W of
    // each of the first three rows, which is the transpose of m, so each GPU row here is a column of m. Getting this backwards
    // drops translation from every bone matrix while leaving rotation and scale intact, which looks like a corrupt pose rather
    // than nothing moving.
    static void WriteMatrix(byte[] data, int slot, in Matrix4x4 m)
    {
        var block = new Std140Block(BytesPerBone);
        block.WriteRows(0, CameraData.Rows(m, 3));
        block.ToByteArray().CopyTo(data, slot * BytesPerBone);
    }

    public static BonePaletteUbo FillIdentity(ReadOnlySpan<Vector4> modelRows = default, int byteSize = DefaultByteSize)
    {
        Span<Vector4> rows = stackalloc Vector4[3];
        if (modelRows.IsEmpty)
        {
            rows[0] = new Vector4(1, 0, 0, 0);
            rows[1] = new Vector4(0, 1, 0, 0);
            rows[2] = new Vector4(0, 0, 1, 0);
        }
        else
        {
            if (modelRows.Length != 3)
                throw new ArgumentException("modelRows must have exactly 3 rows (mat3x4)", nameof(modelRows));
            modelRows.CopyTo(rows);
        }

        var bone = new Std140Block(BytesPerBone);
        bone.WriteRows(0, rows);
        byte[] oneBone = bone.ToByteArray();

        var data = new byte[byteSize];
        for (int offset = 0; offset + BytesPerBone <= byteSize; offset += BytesPerBone)
            oneBone.CopyTo(data, offset);
        return new BonePaletteUbo(data);
    }

    public byte[] ToByteArray() => (byte[])_data.Clone();
}
