using System.Globalization;
using System.Numerics;
using System.Text;
using BfShaderSharp.Pack.Records;
using KingRenderingSharp.Content.Assets.Manifests;

namespace KingRenderingSharp.Content.Packs;

/// <summary>A pack's skeleton as a renderer needs it: the bind-pose world matrix of each bone, and the <c>.skeleton.json</c> the pose code reads.</summary>
/// <remarks>
/// World = scale * rotation * translation * parent's world, with scale left out when the skeleton's scaling mode is None, and, under Maya or Softimage scaling, a
/// child with the segment-scale-compensate flag dividing its parent's basis rows by the parent's own local scale first. The flags are the raw words of the model
/// file: bits 8 and 9 of the skeleton's give the scaling mode, bits 12 to 14 of a bone's the rotation kind (0 quaternion, 1 Euler XYZ) and bit 23 of a bone's
/// segment scale compensate. The palette is two segments, smooth slots then rigid ones, in one list; the inverse matrices cover the smooth ones only.
/// </remarks>
static class PackSkeleton
{
    const uint SegmentScaleCompensateBit = 0x800000;

    public static int ScalingMode(SkeletonRecord skeleton) => (int)(skeleton.Flags >> 8) & 3;

    static bool IsQuaternion(BoneRecord bone) => (bone.Flags >> 12 & 7) == 0;

    static bool SegmentScaleCompensate(BoneRecord bone) => (bone.Flags & SegmentScaleCompensateBit) != 0;

    public static Matrix4x4[] BoneWorldMatrices(SkeletonRecord skeleton)
    {
        var bones = skeleton.Bones;
        int mode = ScalingMode(skeleton);
        var world = new Matrix4x4[bones.Count];
        for (int i = 0; i < bones.Count; i++)
        {
            BoneRecord b = bones[i];
            Matrix4x4 rotation = IsQuaternion(b)
                ? Matrix4x4.CreateFromQuaternion(new Quaternion(b.Rotation[0], b.Rotation[1], b.Rotation[2], b.Rotation[3]))
                : Matrix4x4.CreateRotationX(b.Rotation[0]) * Matrix4x4.CreateRotationY(b.Rotation[1]) * Matrix4x4.CreateRotationZ(b.Rotation[2]);
            Matrix4x4 translate = Matrix4x4.CreateTranslation(b.Position[0], b.Position[1], b.Position[2]);
            Matrix4x4 local = mode == 0 ? rotation * translate : Matrix4x4.CreateScale(b.Scale[0], b.Scale[1], b.Scale[2]) * rotation * translate;

            // Parents come before their children in a skeleton, so one forward pass is enough.
            if (b.Parent < 0 || b.Parent >= i)
            {
                world[i] = local;
                continue;
            }

            Matrix4x4 parent = world[b.Parent];
            if (mode >= 2 && SegmentScaleCompensate(b)) parent = DescaleRows(parent, bones[b.Parent].Scale);
            world[i] = local * parent;
        }

        return world;
    }

    // Divides a world matrix's three basis rows by a scale, leaving its translation row alone.
    static Matrix4x4 DescaleRows(Matrix4x4 m, float[] scale)
    {
        if (scale[0] == 1f && scale[1] == 1f && scale[2] == 1f) return m;
        float ix = scale[0] != 0f ? 1f / scale[0] : 0f, iy = scale[1] != 0f ? 1f / scale[1] : 0f, iz = scale[2] != 0f ? 1f / scale[2] : 0f;
        m.M11 *= ix; m.M12 *= ix; m.M13 *= ix;
        m.M21 *= iy; m.M22 *= iy; m.M23 *= iy;
        m.M31 *= iz; m.M32 *= iz; m.M33 *= iz;
        return m;
    }

    /// <summary>The skeleton as the pose code reads it, or null for a skeleton without bones.</summary>
    public static SkeletonManifest? ToManifest(SkeletonRecord skeleton)
    {
        if (skeleton.Bones.Count == 0) return null;
        var matrixToBone = skeleton.MatrixToBone;
        var inverse = skeleton.InverseMatrices;

        // A bone carries its own slot number in each segment (-1 for none), so counting them reads the two counts the skeleton stores.
        int smooth = skeleton.Bones.Count(b => b.Smooth >= 0);
        int rigid = skeleton.Bones.Count(b => b.Rigid >= 0);
        if (smooth + rigid != matrixToBone.Count)
        {
            if (inverse.Count > 0 && inverse.Count <= matrixToBone.Count) smooth = inverse.Count;
            smooth = Math.Min(smooth, matrixToBone.Count);
            rigid = matrixToBone.Count - smooth;
        }

        SkeletonManifest manifest = new()
        {
            ScalingMode = (SkeletonScalingMode)ScalingMode(skeleton),
            SmoothMatrixCount = smooth,
            RigidMatrixCount = rigid,
            MatrixToBoneList = [.. matrixToBone.Select(m => (int)m)],
        };
        foreach (BoneRecord b in skeleton.Bones)
        {
            manifest.Bones.Add(new BoneManifestEntry
            {
                Name = b.Name, ParentIndex = b.Parent, SmoothMatrixIndex = b.Smooth, RigidMatrixIndex = b.Rigid, BillboardIndex = b.Billboard,
                SegmentScaleCompensate = SegmentScaleCompensate(b), Scale = [.. b.Scale], Rotation = [.. b.Rotation], RotationIsQuaternion = IsQuaternion(b), Position = [.. b.Position],
            });
        }

        // One inverse-bind matrix per smooth slot. A skeleton that stores fewer gets the inverse of the bone's own bind-pose world matrix, which is what the stored one undoes.
        Matrix4x4[]? bindWorld = smooth > inverse.Count ? BoneWorldMatrices(skeleton) : null;
        for (int i = 0; i < smooth; i++)
            manifest.InverseModelMatrices.Add(i < inverse.Count ? [.. inverse[i]] : DerivedInverse(bindWorld!, matrixToBone, i));
        return manifest;
    }

    /// <summary>The skeleton's JSON, or null for a skeleton without bones.</summary>
    public static string? ToJson(SkeletonRecord skeleton)
    {
        if (skeleton.Bones.Count == 0) return null;
        var matrixToBone = skeleton.MatrixToBone;
        var inverse = skeleton.InverseMatrices;

        // A bone carries its own slot number in each segment (-1 for none), so counting them reads the two counts the skeleton stores.
        int smooth = skeleton.Bones.Count(b => b.Smooth >= 0);
        int rigid = skeleton.Bones.Count(b => b.Rigid >= 0);
        if (smooth + rigid != matrixToBone.Count)
        {
            if (inverse.Count > 0 && inverse.Count <= matrixToBone.Count) smooth = inverse.Count;
            smooth = Math.Min(smooth, matrixToBone.Count);
            rigid = matrixToBone.Count - smooth;
        }

        StringBuilder sb = new();
        sb.AppendLine("{");
        sb.AppendLine($"  \"scaling_mode\": {ScalingMode(skeleton)},");
        sb.AppendLine($"  \"smooth_matrix_count\": {smooth},");
        sb.AppendLine($"  \"rigid_matrix_count\": {rigid},");
        sb.AppendLine("  \"bones\": [");
        for (int i = 0; i < skeleton.Bones.Count; i++)
        {
            BoneRecord b = skeleton.Bones[i];
            sb.AppendLine("    {");
            sb.AppendLine($"      \"name\": {Quote(b.Name)},");
            sb.AppendLine($"      \"parent_index\": {b.Parent},");
            sb.AppendLine($"      \"smooth_matrix_index\": {b.Smooth},");
            sb.AppendLine($"      \"rigid_matrix_index\": {b.Rigid},");
            sb.AppendLine($"      \"billboard_index\": {b.Billboard},");
            sb.AppendLine($"      \"segment_scale_compensate\": {(SegmentScaleCompensate(b) ? "true" : "false")},");
            sb.AppendLine($"      \"scale\": [{string.Join(", ", b.Scale.Select(F))}],");
            sb.AppendLine($"      \"rotation\": [{string.Join(", ", b.Rotation.Select(F))}],");
            sb.AppendLine($"      \"rotation_is_quaternion\": {(IsQuaternion(b) ? "true" : "false")},");
            sb.AppendLine($"      \"position\": [{string.Join(", ", b.Position.Select(F))}]");
            sb.AppendLine(i == skeleton.Bones.Count - 1 ? "    }" : "    },");
        }

        sb.AppendLine("  ],");
        sb.AppendLine($"  \"matrix_to_bone_list\": [{string.Join(", ", matrixToBone)}],");

        // One inverse-bind matrix per smooth slot. A skeleton that stores fewer gets the inverse of the bone's own bind-pose world matrix, which is what the stored one undoes.
        Matrix4x4[]? bindWorld = smooth > inverse.Count ? BoneWorldMatrices(skeleton) : null;
        sb.AppendLine("  \"inverse_model_matrices\": [");
        for (int i = 0; i < smooth; i++)
        {
            float[] m = i < inverse.Count ? inverse[i] : DerivedInverse(bindWorld!, matrixToBone, i);
            sb.Append($"    [{string.Join(", ", m.Select(F))}]");
            sb.AppendLine(i == smooth - 1 ? "" : ",");
        }

        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // The twelve floats of the inverse of a bone's bind world matrix, as rows of the GPU's 3x4: each is a column of the inverse.
    static float[] DerivedInverse(Matrix4x4[] bindWorld, IReadOnlyList<ushort> matrixToBone, int slot)
    {
        int bone = slot < matrixToBone.Count ? matrixToBone[slot] : -1;
        if (bone < 0 || bone >= bindWorld.Length || !Matrix4x4.Invert(bindWorld[bone], out Matrix4x4 inv))
            return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0];
        return [inv.M11, inv.M21, inv.M31, inv.M41, inv.M12, inv.M22, inv.M32, inv.M42, inv.M13, inv.M23, inv.M33, inv.M43];
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    internal static string Quote(string text) => System.Text.Json.JsonSerializer.Serialize(text);
}
