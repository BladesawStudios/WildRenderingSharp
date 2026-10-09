using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets.Manifests;

/// <summary>Deserialized <c>&lt;Model&gt;.skeleton.json</c>, everything needed to build the bone palette at bind pose or at a pose from a <see cref="SkeletalAnimManifest"/>.</summary>
public sealed class SkeletonManifest
{
    // How the bones' scales combine down the hierarchy; see SkeletonScalingMode.
    [JsonPropertyName("scaling_mode")] public SkeletonScalingMode ScalingMode { get; set; } = SkeletonScalingMode.Standard;
    // Length of the palette's smooth segment (FSKL[0x3A]), or -1 when the manifest does not carry it; read SmoothCount, which derives it then.
    [JsonPropertyName("smooth_matrix_count")] public int SmoothMatrixCount { get; set; } = -1;
    [JsonPropertyName("rigid_matrix_count")] public int RigidMatrixCount { get; set; } = -1;
    [JsonPropertyName("bones")] public List<BoneManifestEntry> Bones { get; set; } = [];
    [JsonPropertyName("matrix_to_bone_list")] public List<int> MatrixToBoneList { get; set; } = [];
    // Smooth slot -> that bone's inverse bind matrix, 12 floats (3 rows of 4) each, parallel to the first SmoothCount entries of MatrixToBoneList.
    [JsonPropertyName("inverse_model_matrices")] public List<float[]> InverseModelMatrices { get; set; } = [];

    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static SkeletonManifest Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<SkeletonManifest>(stream, JsonOptions)
            ?? throw new InvalidDataException($"'{path}' did not deserialize to a skeleton manifest.");
    }

    public int SmoothCount
    {
        get
        {
            int count = SmoothMatrixCount >= 0 ? SmoothMatrixCount : Bones.Count(b => b.SmoothMatrixIndex >= 0);
            return Math.Clamp(count, 0, MatrixToBoneList.Count);
        }
    }

    public int RigidCount => MatrixToBoneList.Count - SmoothCount;

    // The inverse bind matrices as matrices, worked out once from the loaded manifest: callers must not change them.
    public Matrix4x4[] InverseModelMatricesAsMatrices() => _inverseMatrices ??= BuildInverseMatrices();

    Matrix4x4[]? _inverseMatrices;

    Matrix4x4[] BuildInverseMatrices()
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
