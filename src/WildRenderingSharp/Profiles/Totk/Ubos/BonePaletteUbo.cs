using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>
/// TotK's skinning matrix palette (the engine's <c>g3d_SkeletonUniformBlock</c>, the shader symbol <c>_Mtx</c>), binding 2. 48 bytes per matrix: a mat3x4 of three vec4
/// rows, row-vector convention (a vertex is <c>v * M</c>; composition is "apply the first operand, then the second"; see <c>Mat4Math.Multiply</c> and
/// <c>SkeletonPose</c>). The vertex shader unpacks two 16-bit bone indices per blend-index float and indexes this block directly, so an index is a plain array index
/// into <see cref="Build"/>'s output, not a byte offset.
/// </summary>
/// <remarks>
/// <para>
/// Layout recovered from the game via Ghidra. <c>nn::g3d2::SkeletonObj::SetupBlockBufferImpl</c> (0x7100081dfc) sizes the buffer as <c>(smoothCount + rigidCount) * 0x30</c>
/// from two ushorts at <c>FSKL[0x3A]</c> and <c>FSKL[0x3C]</c>. <c>SkeletonObj::CalculateSkeleton</c> (0x71000824a8) fills it in two segments, both reading the same
/// bone-index array (<c>FSKL[0x18]</c>, <c>MatrixToBoneList</c>): slots [0, smoothCount) hold <c>InverseModelMatrix[i] * BoneWorld[MatrixToBoneList[i]]</c>, stepping a
/// 0x30-stride inverse-bind array (<c>FSKL[0x20]</c>) in lock step; slots [smoothCount, smoothCount + rigidCount) hold <c>BoneWorld[MatrixToBoneList[smoothCount + j]]</c>,
/// transposed into mat3x4 rows and copied directly with no inverse-bind multiply.
/// </para>
/// <para>
/// The split is easy to get wrong. <c>MatrixToBoneList</c> is one combined array and <c>InverseModelMatrices</c> holds only smoothCount entries, so the inverse-bind list
/// being shorter is normal, not a truncated file. Treating the whole list as smooth applies an invented inverse-bind to every rigid slot and pushes every later index past
/// the real palette.
/// </para>
/// <para>
/// A vertex's <c>vBoneIndices</c> is an absolute slot into the combined array: <c>vertex_skin_count &gt;= 2</c> lands in the smooth segment and
/// <c>vertex_skin_count == 1</c> carries the bone's <c>RigidMatrixIndex</c>, which BFRES already stores offset past the smooth segment. Nothing adds smoothCount a second
/// time (see <c>ShaderLibrary.CompileTool.ExportTestBench</c> for the export side).
/// </para>
/// </remarks>
public sealed class BonePaletteUbo : IUboBlock
{
    public const int BytesPerBone = 48;
    public const int DefaultByteSize = 65536;

    readonly byte[] _data;

    public string Name => "_Mtx";
    public const uint Binding = TotkBindings.Bones;
    public int BindingIndex => (int)Binding;

    BonePaletteUbo(byte[] data) => _data = data;

    /// <summary>Builds the two-segment palette described above.</summary>
    /// <param name="boneWorld">Every bone's current world matrix (bind pose or animated), indexed by bone id.</param>
    /// <param name="matrixToBoneList">Palette slot to bone id (<c>Skeleton.MatrixToBoneList</c>), combined: the first <paramref name="inverseModelMatrices"/>.Length entries are the smooth segment and the rest the rigid segment.</param>
    /// <param name="inverseModelMatrices">Smooth slot to that bone's inverse bind matrix. Its length defines where the rigid segment starts, so it must be exactly the smooth count; <c>SkeletonManifest.InverseModelMatricesAsMatrices</c> guarantees that.</param>
    /// <param name="modelTransform">Right-multiplied onto every matrix (skin, then model): the model's free rotation, which the shaders do not fold in separately for a skinned draw (<c>GsysShape.cTransform</c> is read only at <c>SKIN_COUNT == 0</c>). Defaults to identity.</param>
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

    /// <summary>
    /// <paramref name="m"/> is a <see cref="System.Numerics.Matrix4x4"/> composed the native .NET way: row-vector convention, translation in row 4 (M41-M43). The GPU "rows"
    /// convention used everywhere else here (<c>Mat4Math</c>, <c>EulerRotation</c>, <c>Std140Block.WriteRows</c>) packs translation into the W of each of the first three rows,
    /// which is the transpose of <paramref name="m"/>, so each GPU row here is a column of <paramref name="m"/>. Getting this backwards drops translation from every bone matrix
    /// while leaving rotation and scale intact, which looks like a corrupt pose rather than nothing moving.
    /// </summary>
    static void WriteMatrix(byte[] data, int slot, in Matrix4x4 m)
    {
        var block = new Std140Block(BytesPerBone);
        block.WriteRows(0, [new(m.M11, m.M21, m.M31, m.M41), new(m.M12, m.M22, m.M32, m.M42), new(m.M13, m.M23, m.M33, m.M43)]);
        block.ToByteArray().CopyTo(data, slot * BytesPerBone);
    }

    /// <summary>Tiles one mat3x4 (identity, or <paramref name="modelRows"/> if given) across every bone slot in a <paramref name="byteSize"/> buffer. The fallback for a model with no bones: skinned positions are transformed through this block, not <see cref="ShapeMatrixUbo"/>, so a rotated model needs its rows here. <see cref="Build"/> is the path for anything skinned.</summary>
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
