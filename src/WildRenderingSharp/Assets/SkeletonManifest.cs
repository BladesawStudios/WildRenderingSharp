using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>How a skeleton's bone scales propagate down the hierarchy - <c>(FSKL.flags &gt;&gt; 8) &amp; 3</c>, exactly the value <c>nn::g3d2::SkeletonObj::CalculateWorldMtx</c> (Ghidra 0x710008246c) switches its three <c>CalculateWorldImpl</c> specialisations on.</summary>
public enum SkeletonScalingMode
{
    /// <summary><c>CalculateWorldImpl&lt;CalculateWorldNoScale&gt;</c> (0x7100080b40) - bone scale is never applied at all.</summary>
    None = 0,
    /// <summary><c>CalculateWorldImpl&lt;CalculateWorldStd&gt;</c> (0x7100080d00) - scale multiplies world rows 0/1/2 after the parent multiply, and cascades to children like any other part of the transform.</summary>
    Standard = 1,
    /// <summary><c>CalculateWorldImpl&lt;CalculateWorldMaya&gt;</c> (0x7100080f2c) - Standard, plus segment scale compensate for any bone flagged with it.</summary>
    Maya = 2,
    /// <summary>No dedicated specialisation exists in the shipped binary (the dispatch table at 0x71041da970 runs out at Maya); treated as <see cref="Maya"/>.</summary>
    Softimage = 3,
}

/// <summary>One bone from <c>&lt;Model&gt;.skeleton.json</c>, written by <c>ShaderLibrary.CompileTool.ExportTestBench.ExportSkeleton</c>.</summary>
public sealed class BoneManifestEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("parent_index")] public int ParentIndex { get; set; } = -1;
    /// <summary>This bone's slot in the smooth-skinning segment of the palette, or -1 if it has none. Redundant with <see cref="SkeletonManifest.MatrixToBoneList"/> (its inverse) but kept for reference/debugging - and it is what <see cref="SkeletonManifest.SmoothCount"/> falls back to counting.</summary>
    [JsonPropertyName("smooth_matrix_index")] public int SmoothMatrixIndex { get; set; } = -1;
    /// <summary>
    /// This bone's slot in the palette's rigid segment, or -1 if it has none. This is an ABSOLUTE
    /// palette index - BFRES stores it already offset past the smooth segment (Animal_Bass has 4
    /// smooth slots and its Head bone reports <c>SmoothMatrixIndex 0, RigidMatrixIndex 4</c>, with
    /// <c>MatrixToBoneList[4]</c> pointing back at Head). Never add
    /// <see cref="SkeletonManifest.SmoothCount"/> to it.
    /// </summary>
    [JsonPropertyName("rigid_matrix_index")] public int RigidMatrixIndex { get; set; } = -1;
    /// <summary>Billboard mode index, or -1. Not implemented by <see cref="WildRenderingSharp.Rendering.SkeletonPose"/> (the game's own <c>SkeletonObj::CalculateBillboardMtx</c> needs a camera); carried so a billboarded bone is at least identifiable.</summary>
    [JsonPropertyName("billboard_index")] public int BillboardIndex { get; set; } = -1;
    /// <summary>Bone flag bit 23. Under <see cref="SkeletonScalingMode.Maya"/> this bone divides its parent's world basis rows by the PARENT'S own local scale before composing, so the parent's scale does not cascade into it - see <see cref="WildRenderingSharp.Rendering.SkeletonPose"/>.</summary>
    [JsonPropertyName("segment_scale_compensate")] public bool SegmentScaleCompensate { get; set; }
    [JsonPropertyName("scale")] public float[] Scale { get; set; } = [1, 1, 1];
    /// <summary>Quaternion (x, y, z, w) if <see cref="RotationIsQuaternion"/>, else Euler XYZ radians in (x, y, z, unused).</summary>
    [JsonPropertyName("rotation")] public float[] Rotation { get; set; } = [0, 0, 0, 1];
    [JsonPropertyName("rotation_is_quaternion")] public bool RotationIsQuaternion { get; set; } = true;
    [JsonPropertyName("position")] public float[] Position { get; set; } = [0, 0, 0];

    public Vector3 ScaleVec => new(Scale[0], Scale[1], Scale[2]);
    public Vector3 PositionVec => new(Position[0], Position[1], Position[2]);

    public Matrix4x4 RotationMatrix() => RotationIsQuaternion
        ? Matrix4x4.CreateFromQuaternion(new Quaternion(Rotation[0], Rotation[1], Rotation[2], Rotation[3]))
        : EulerXyzToMatrix(Rotation[0], Rotation[1], Rotation[2]);

    /// <summary>
    /// Euler XYZ radians to a row-vector rotation matrix: <c>Rx * Ry * Rz</c> (apply X, then Y,
    /// then Z). Confirmed against <c>nn::g3d2::SkeletalAnimObj::ApplyToImpl&lt;nn::g3d::EulerToMtx&gt;</c>
    /// (Ghidra 0x710007a1a0) and <c>SkeletonObj::ClearLocalMtx</c> (0x7100ad6978), whose first
    /// output row is <c>(cy*cz, cy*sz, -sy)</c> - the row 0 of exactly this product, and NOT of the
    /// reverse order.
    /// </summary>
    public static Matrix4x4 EulerXyzToMatrix(float x, float y, float z) =>
        Matrix4x4.CreateRotationX(x) * Matrix4x4.CreateRotationY(y) * Matrix4x4.CreateRotationZ(z);

    /// <summary>The inverse of <see cref="EulerXyzToMatrix"/> - used only when an anim's rotation mode disagrees with the skeleton's, so a curve-less bone's bind rotation can still be expressed in the anim's own representation.</summary>
    public static Vector3 MatrixToEulerXyz(in Matrix4x4 m)
    {
        float sy = Math.Clamp(-m.M13, -1f, 1f);
        // |sy| == 1 is gimbal lock: cy == 0 collapses X and Z into one angle, so pin Z at 0 and
        // put the whole remaining rotation on X, the same choice every decomposition makes.
        if (MathF.Abs(m.M13) > 0.99999f)
            return new Vector3(MathF.Atan2(-m.M32, m.M22), MathF.Asin(sy), 0f);
        return new Vector3(MathF.Atan2(m.M23, m.M33), MathF.Asin(sy), MathF.Atan2(m.M12, m.M11));
    }
}

/// <summary>
/// Deserialized <c>&lt;Model&gt;.skeleton.json</c> - everything needed to build the real
/// <see cref="WildRenderingSharp.Profiles.Totk.Ubos.BonePaletteUbo"/> at bind pose (via
/// <see cref="WildRenderingSharp.Rendering.SkeletonPose.BindPoseWorldMatrices"/>), or at any animated
/// pose given a <see cref="SkeletalAnimManifest"/>.
///
/// THE PALETTE IS TWO SEGMENTS AND <see cref="MatrixToBoneList"/> COVERS BOTH - it has
/// <see cref="SmoothCount"/> + <see cref="RigidCount"/> entries, not just the smooth ones, and
/// <see cref="InverseModelMatrices"/> is parallel to its smooth PREFIX only. See
/// <see cref="WildRenderingSharp.Profiles.Totk.Ubos.BonePaletteUbo"/>'s remarks for the Ghidra
/// citations and what goes wrong when the split is missed.
/// </summary>
public sealed class SkeletonManifest
{
    /// <summary>See <see cref="SkeletonScalingMode"/>. Defaults to <see cref="SkeletonScalingMode.Standard"/> for a manifest exported before this field existed - the mode that matches the old unconditional walk.</summary>
    [JsonPropertyName("scaling_mode")] public SkeletonScalingMode ScalingMode { get; set; } = SkeletonScalingMode.Standard;
    /// <summary>Length of the palette's smooth segment (<c>FSKL[0x3A]</c>), or -1 in a manifest exported before this field existed - read <see cref="SmoothCount"/> instead, which derives it in that case.</summary>
    [JsonPropertyName("smooth_matrix_count")] public int SmoothMatrixCount { get; set; } = -1;
    /// <summary>Length of the palette's rigid segment (<c>FSKL[0x3C]</c>), or -1 if not exported - read <see cref="RigidCount"/> instead.</summary>
    [JsonPropertyName("rigid_matrix_count")] public int RigidMatrixCount { get; set; } = -1;
    [JsonPropertyName("bones")] public List<BoneManifestEntry> Bones { get; set; } = [];
    /// <summary>Palette slot -&gt; bone index (<c>BfresLibrary.Skeleton.MatrixToBoneList</c>), smooth slots first then rigid ones - <see cref="SmoothCount"/> + <see cref="RigidCount"/> entries in total.</summary>
    [JsonPropertyName("matrix_to_bone_list")] public List<int> MatrixToBoneList { get; set; } = [];
    /// <summary>Smooth slot -&gt; that bone's inverse bind matrix, 12 floats (3 rows of 4) each, parallel to the first <see cref="SmoothCount"/> entries of <see cref="MatrixToBoneList"/>.</summary>
    [JsonPropertyName("inverse_model_matrices")] public List<float[]> InverseModelMatrices { get; set; } = [];

    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static SkeletonManifest Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<SkeletonManifest>(stream, JsonOptions)
            ?? throw new InvalidDataException($"'{path}' did not deserialize to a skeleton manifest.");
    }

    /// <summary>
    /// The real length of the palette's smooth segment, clamped to what
    /// <see cref="MatrixToBoneList"/> can actually supply. A manifest exported before
    /// <see cref="SmoothMatrixCount"/> existed gets it counted off the bones' own
    /// <see cref="BoneManifestEntry.SmoothMatrixIndex"/> - which is the same number, and which also
    /// makes those older manifests split correctly here even though they over-wrote the
    /// <see cref="InverseModelMatrices"/> array.
    /// </summary>
    public int SmoothCount
    {
        get
        {
            int count = SmoothMatrixCount >= 0 ? SmoothMatrixCount : Bones.Count(b => b.SmoothMatrixIndex >= 0);
            return Math.Clamp(count, 0, MatrixToBoneList.Count);
        }
    }

    /// <summary>The real length of the palette's rigid segment - whatever <see cref="MatrixToBoneList"/> has left after the smooth prefix.</summary>
    public int RigidCount => MatrixToBoneList.Count - SmoothCount;

    /// <summary>
    /// Converts each row of 12 floats (3 GPU-style rows of 4 - translation in each row's 4th
    /// component, exactly the buffer layout <c>ExportTestBench.ExportSkeleton</c> writes) into a
    /// NATIVE <see cref="System.Numerics.Matrix4x4"/> (row-vector: translation in row 4), the form
    /// <see cref="WildRenderingSharp.Rendering.SkeletonPose"/>'s bone-hierarchy math and
    /// <c>BonePaletteUbo.Build</c>'s multiplication both use. This is the TRANSPOSE of a naive
    /// row-major read (see <c>BonePaletteUbo.WriteMatrix</c>'s remarks for the same conversion in
    /// the opposite direction) - getting it backwards silently drops every bone's translation.
    ///
    /// Exactly <see cref="SmoothCount"/> entries come back: an older manifest that wrote one row
    /// per COMBINED slot is truncated to its smooth prefix (which was always the correct part), and
    /// a manifest short of a few rows is padded with identity rather than throwing.
    /// </summary>
    public Matrix4x4[] InverseModelMatricesAsMatrices()
    {
        int smooth = SmoothCount;
        var result = new Matrix4x4[smooth];
        for (int i = 0; i < smooth; i++)
            result[i] = i < InverseModelMatrices.Count ? RowsToMatrix(InverseModelMatrices[i]) : Matrix4x4.Identity;
        return result;
    }

    static Matrix4x4 RowsToMatrix(float[] r) => new(
        r[0], r[4], r[8], 0,
        r[1], r[5], r[9], 0,
        r[2], r[6], r[10], 0,
        r[3], r[7], r[11], 1);
}
