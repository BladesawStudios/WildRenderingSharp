using System.Numerics;
using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>One bone from <c>&lt;Model&gt;.skeleton.json</c>, written by <c>ShaderLibrary.CompileTool.ExportTestBench.ExportSkeleton</c>.</summary>
public sealed class BoneManifestEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("parent_index")] public int ParentIndex { get; set; } = -1;
    /// <summary>This bone's slot in the smooth-skinning segment of the palette, or -1 if it has none. Redundant with <see cref="SkeletonManifest.MatrixToBoneList"/> (its inverse) but kept for reference/debugging - and it is what <see cref="SkeletonManifest.SmoothCount"/> falls back to counting.</summary>
    [JsonPropertyName("smooth_matrix_index")] public int SmoothMatrixIndex { get; set; } = -1;
    /// <summary>This bone's slot in the palette's rigid segment, or -1 if it has none. An absolute palette index: BFRES stores it already offset past the smooth segment (Animal_Bass has 4 smooth slots and its Head bone reports <c>SmoothMatrixIndex 0, RigidMatrixIndex 4</c>), so never add <see cref="SkeletonManifest.SmoothCount"/> to it.</summary>
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
