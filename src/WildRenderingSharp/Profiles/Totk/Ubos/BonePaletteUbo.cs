using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>
/// TotK's skinning matrix palette - the real engine calls the containing GPU buffer
/// <c>g3d_SkeletonUniformBlock</c>; the shader-side symbol this codebase has been calling
/// <c>_Mtx</c> is the same buffer, binding 2. 48 bytes per matrix (a mat3x4 - three vec4 rows,
/// row-vector convention: a vertex is transformed as <c>v * M</c>, and hierarchy/skin composition
/// throughout this codebase is "apply the first operand, then the second" - see
/// <c>WildRenderingSharp.Rendering.Mat4Math.Multiply</c> and <c>WildRenderingSharp.Rendering.SkeletonPose</c>).
/// The vertex shader unpacks two 16-bit bone indices per blend-index float
/// (<c>(floatBitsToUint(v) &gt;&gt; 16) * 48</c> and <c>(floatBitsToInt(v) &amp; 0xFFFF) * 48</c>)
/// and indexes this block directly - which is a plain array index into <see cref="Build"/>'s
/// output, not a byte offset the caller needs to compute.
///
/// LAYOUT - reverse engineered from the running game via Ghidra, not guessed.
/// <c>nn::g3d2::SkeletonObj::SetupBlockBufferImpl</c> (0x7100081dfc) sizes the buffer as
/// <c>(smoothCount + rigidCount) * 0x30</c>, reading those two ushorts from the resource skeleton
/// at <c>FSKL[0x3A]</c> and <c>FSKL[0x3C]</c>. <c>SkeletonObj::CalculateSkeleton</c> (0x71000824a8)
/// then fills it in two segments, and BOTH read out of the SAME bone-index array
/// (<c>FSKL[0x18]</c>, i.e. <c>BfresLibrary.Skeleton.MatrixToBoneList</c>):
///   - slots [0, smoothCount): <c>Smooth[i] = InverseModelMatrix[i] * BoneWorld[MatrixToBoneList[i]]</c>,
///     stepping a 0x30-stride inverse-bind array (<c>FSKL[0x20]</c>) in lock-step;
///   - slots [smoothCount, smoothCount + rigidCount):
///     <c>Rigid[j] = BoneWorld[MatrixToBoneList[smoothCount + j]]</c>, transposed into mat3x4 rows
///     and copied DIRECTLY, with no inverse-bind multiply.
///
/// THE SPLIT IS THE PART THAT IS EASY TO GET WRONG. <c>MatrixToBoneList</c> is one combined array
/// of smoothCount + rigidCount entries, and <c>InverseModelMatrices</c> holds only smoothCount - so
/// the inverse-bind list being SHORTER than the bone-index list is the normal case, not a truncated
/// file. Treating the whole list as smooth inflates smoothCount, applies an invented inverse-bind
/// to every rigid slot (pinning those parts near the bind pose while the smooth-skinned mesh around
/// them animates), and pushes every subsequent index out past the real palette.
///
/// A vertex's own <c>vBoneIndices</c> attribute is an ABSOLUTE slot into this combined array in
/// every skinned case - <c>vertex_skin_count &gt;= 2</c> lands in the smooth segment, and
/// <c>vertex_skin_count == 1</c> carries the target bone's <c>Bone.RigidMatrixIndex</c>, which
/// BFRES already stores offset past the smooth segment. Nothing adds smoothCount to it a second
/// time; see <c>ShaderLibrary.CompileTool.ExportTestBench</c> for the export side.
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

    /// <summary>
    /// Builds the real two-segment palette described above.
    /// </summary>
    /// <param name="boneWorld">Every bone's current WORLD matrix (bind pose, or an animated pose - same shape either way), indexed by bone id.</param>
    /// <param name="matrixToBoneList">
    /// Palette slot -&gt; bone id (<c>Skeleton.MatrixToBoneList</c>), COMBINED: the first
    /// <paramref name="inverseModelMatrices"/>.Length entries are the smooth segment and everything
    /// after them is the rigid segment.
    /// </param>
    /// <param name="inverseModelMatrices">
    /// Smooth slot -&gt; that bone's inverse bind matrix (<c>Skeleton.InverseModelMatrices</c>).
    /// Its LENGTH defines where the rigid segment starts, so it must be exactly the smooth count -
    /// <c>SkeletonManifest.InverseModelMatricesAsMatrices</c> guarantees that.
    /// </param>
    /// <param name="modelTransform">
    /// Right-multiplied onto every matrix (row-vector: skin-then-model) - the viewer's own
    /// free-rotation of the whole model, which the real shaders never fold in separately for a
    /// skinned draw (<c>GsysShape.cTransform</c> is only read at <c>SKIN_COUNT == 0</c>, and this
    /// codebase's shapes are never drawn at SKIN_COUNT == 0 with an identity palette any more).
    /// Defaults to identity.
    /// </param>
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
    /// <paramref name="m"/> is a <see cref="System.Numerics.Matrix4x4"/> built and composed the
    /// NATIVE .NET way (<c>Matrix4x4.CreateScale/CreateFromQuaternion/CreateTranslation</c> and
    /// <c>*</c>) - row-vector convention, translation in row 4 (M41-M43). WildRenderingSharp's own std140/GPU
    /// "rows" convention used EVERYWHERE ELSE in this codebase (<c>Mat4Math</c>,
    /// <c>EulerRotation</c>, <c>Std140Block.WriteRows</c>) instead packs translation into the W
    /// component of each of the first 3 rows - which is exactly the TRANSPOSE of <paramref name="m"/>'s
    /// own row-vector layout, so each GPU row here is a COLUMN of <paramref name="m"/>, not a row.
    /// Getting this backwards drops translation from every bone matrix entirely (rows 0-2 of a
    /// native affine matrix have a zero 4th component) while leaving rotation/scale intact - which
    /// looks exactly like "the pose is corrupt" rather than "nothing moved".
    /// </summary>
    static void WriteMatrix(byte[] data, int slot, in Matrix4x4 m)
    {
        var block = new Std140Block(BytesPerBone);
        block.WriteRows(0, [new(m.M11, m.M21, m.M31, m.M41), new(m.M12, m.M22, m.M32, m.M42), new(m.M13, m.M23, m.M33, m.M43)]);
        block.ToByteArray().CopyTo(data, slot * BytesPerBone);
    }

    /// <summary>
    /// Tiles one mat3x4 (identity, or <paramref name="modelRows"/> if given - used when the
    /// viewer rotates the model, since skinned positions are transformed through THIS block, not
    /// <see cref="ShapeMatrixUbo"/>) across every bone slot in a <paramref name="byteSize"/>
    /// buffer. This is the "no skeleton" fallback for a model with no bones at all -
    /// <see cref="Build"/> is the real path for anything skinned.
    /// </summary>
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
