using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>
/// Deserialized <c>&lt;Model&gt;.skeleton.json</c>: everything needed to build the <c>BonePaletteUbo</c> at bind pose (via <see
/// cref="WildRenderingSharp.Rendering.SkeletonPose.BindPoseWorldMatrices"/>) or at an animated pose given a <see
/// cref="SkeletalAnimManifest"/>. The palette has two segments and <see cref="MatrixToBoneList"/> covers both (<see
/// cref="SmoothCount"/> + <see cref="RigidCount"/> entries), while <see cref="InverseModelMatrices"/> is parallel to the smooth
/// prefix only; see <c>BonePaletteUbo</c> for the Ghidra citations.
/// </summary>
public sealed class SkeletonManifest
{
    // See SkeletonScalingMode. Defaults to Standard for a manifest exported before this field existed - the mode that matches
    // the old unconditional walk.
    [JsonPropertyName("scaling_mode")] public SkeletonScalingMode ScalingMode { get; set; } = SkeletonScalingMode.Standard;
    // Length of the palette's smooth segment (FSKL[0x3A]), or -1 in a manifest exported before this field existed - read
    // SmoothCount instead, which derives it in that case.
    [JsonPropertyName("smooth_matrix_count")] public int SmoothMatrixCount { get; set; } = -1;
    [JsonPropertyName("rigid_matrix_count")] public int RigidMatrixCount { get; set; } = -1;
    [JsonPropertyName("bones")] public List<BoneManifestEntry> Bones { get; set; } = [];
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
    /// The length of the palette's smooth segment, clamped to what <see cref="MatrixToBoneList"/> can supply. A manifest exported
    /// before <see cref="SmoothMatrixCount"/> existed gets it counted off the bones' <see
    /// cref="BoneManifestEntry.SmoothMatrixIndex"/>, the same number, which also splits those manifests correctly.
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
    /// Converts each row of 12 floats (3 GPU-style rows of 4, translation in each row's 4th component, as
    /// <c>ExportTestBench.ExportSkeleton</c> writes) into a native row-vector <see cref="System.Numerics.Matrix4x4"/> (translation
    /// in row 4), the form the bone-hierarchy math and <c>BonePaletteUbo.Build</c> use. This is the transpose of a naive row-major
    /// read; getting it backwards silently drops every bone's translation.
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
