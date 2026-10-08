using System.Numerics;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Rendering;

/// <summary>
/// Walks a <see cref="SkeletonManifest"/>'s hierarchy into per-bone world matrices, at bind pose or (given a <see
/// cref="SkeletalAnimManifest"/> and a frame) at an animated pose.
/// </summary>
public static class SkeletonPose
{
    /// <summary>A bone's resolved local transform for one frame - the inputs <see cref="World"/> composes.</summary>
    public readonly record struct BoneLocal(Vector3 Scale, Matrix4x4 Rotation, Vector3 Translation);

    public static Matrix4x4[] BindPoseWorldMatrices(SkeletonManifest skel)
    {
        var locals = new BoneLocal[skel.Bones.Count];
        for (int i = 0; i < skel.Bones.Count; i++)
        {
            var b = skel.Bones[i];
            locals[i] = new BoneLocal(b.ScaleVec, b.RotationMatrix(), b.PositionVec);
        }
        return World(skel, locals, sscOverride: null);
    }

    public static Matrix4x4[] AnimatedWorldMatrices(SkeletonManifest skel, SkeletalAnimManifest anim, float frame)
    {
        var byName = new Dictionary<string, BoneAnimManifestEntry>(anim.BoneAnims.Count, StringComparer.Ordinal);
        foreach (var ba in anim.BoneAnims)
            byName[ba.BoneName] = ba;

        var locals = new BoneLocal[skel.Bones.Count];
        // An animated bone's segment-scale-compensate state comes from the anim: ApplyToImpl copies the anim result's flag bits 23-27 over the bone's own.
        bool[]? ssc = null;
        for (int i = 0; i < skel.Bones.Count; i++)
        {
            var b = skel.Bones[i];
            if (byName.TryGetValue(b.Name, out var boneAnim))
            {
                locals[i] = ComposeAnimatedLocal(b, boneAnim, anim.RotationIsQuaternion, frame);
                if (boneAnim.SegmentScaleCompensate != b.SegmentScaleCompensate)
                {
                    ssc ??= DefaultSsc(skel);
                    ssc[i] = boneAnim.SegmentScaleCompensate;
                }
            }
            else
            {
                locals[i] = new BoneLocal(b.ScaleVec, b.RotationMatrix(), b.PositionVec);
            }
        }
        return World(skel, locals, ssc);
    }

    static bool[] DefaultSsc(SkeletonManifest skel)
    {
        var ssc = new bool[skel.Bones.Count];
        for (int i = 0; i < skel.Bones.Count; i++)
            ssc[i] = skel.Bones[i].SegmentScaleCompensate;
        return ssc;
    }

    public static Matrix4x4[] World(SkeletonManifest skel, BoneLocal[] locals, bool[]? sscOverride)
    {
        var mode = skel.ScalingMode;
        bool applyScale = mode != SkeletonScalingMode.None;
        bool compensate = mode is SkeletonScalingMode.Maya or SkeletonScalingMode.Softimage;

        var world = new Matrix4x4[skel.Bones.Count];
        for (int i = 0; i < skel.Bones.Count; i++)
        {
            var b = skel.Bones[i];
            BoneLocal l = locals[i];
            Matrix4x4 local = applyScale
                ? Matrix4x4.CreateScale(l.Scale) * l.Rotation * Matrix4x4.CreateTranslation(l.Translation)
                : l.Rotation * Matrix4x4.CreateTranslation(l.Translation);

            int parent = b.ParentIndex;
            if (parent < 0 || parent >= i)
            {
                world[i] = local;
                continue;
            }

            Matrix4x4 parentWorld = world[parent];
            bool ssc = sscOverride is null ? b.SegmentScaleCompensate : sscOverride[i];
            if (compensate && ssc)
                parentWorld = DescaleRows(parentWorld, locals[parent].Scale);
            world[i] = local * parentWorld;
        }
        return world;
    }

    public static Matrix4x4 DescaleRows(Matrix4x4 m, Vector3 scale)
    {
        if (scale.X == 1f && scale.Y == 1f && scale.Z == 1f)
            return m;
        float ix = scale.X != 0f ? 1f / scale.X : 0f;
        float iy = scale.Y != 0f ? 1f / scale.Y : 0f;
        float iz = scale.Z != 0f ? 1f / scale.Z : 0f;
        m.M11 *= ix; m.M12 *= ix; m.M13 *= ix;
        m.M21 *= iy; m.M22 *= iy; m.M23 *= iy;
        m.M31 *= iz; m.M32 *= iz; m.M33 *= iz;
        return m;
    }

    static BoneLocal ComposeAnimatedLocal(BoneManifestEntry bone, BoneAnimManifestEntry anim, bool quaternion, float frame)
    {
        Vector3 scale = anim.UseScale ? new(anim.BaseScale[0], anim.BaseScale[1], anim.BaseScale[2]) : bone.ScaleVec;
        Vector3 translate = anim.UseTranslate ? new(anim.BaseTranslate[0], anim.BaseTranslate[1], anim.BaseTranslate[2]) : bone.PositionVec;

        // Rotation lives in whichever representation the anim declares, so a curve-less bone's bind rotation is converted to it before curves overwrite parts of it. The two agree for real files; converting keeps a mismatch from producing garbage.
        Vector4 rotate;
        if (anim.UseRotate)
            rotate = new Vector4(anim.BaseRotate[0], anim.BaseRotate[1], anim.BaseRotate[2], anim.BaseRotate[3]);
        else if (quaternion == bone.RotationIsQuaternion)
            rotate = new Vector4(bone.Rotation[0], bone.Rotation[1], bone.Rotation[2], bone.Rotation[3]);
        else if (quaternion)
        {
            var q = Quaternion.CreateFromRotationMatrix(bone.RotationMatrix());
            rotate = new Vector4(q.X, q.Y, q.Z, q.W);
        }
        else
        {
            Vector3 e = BoneManifestEntry.MatrixToEulerXyz(bone.RotationMatrix());
            rotate = new Vector4(e.X, e.Y, e.Z, 0f);
        }

        foreach (var curve in anim.Curves)
        {
            float value = EvaluateCurve(curve, frame);
            switch (curve.TargetOffset)
            {
                case 4: scale.X = value; break;
                case 8: scale.Y = value; break;
                case 12: scale.Z = value; break;
                case 16: translate.X = value; break;
                case 20: translate.Y = value; break;
                case 24: translate.Z = value; break;
                case 32: rotate.X = value; break;
                case 36: rotate.Y = value; break;
                case 40: rotate.Z = value; break;
                case 44: rotate.W = value; break;
            }
        }

        Matrix4x4 rotation;
        if (quaternion)
        {
            var q = new Quaternion(rotate.X, rotate.Y, rotate.Z, rotate.W);
            rotation = Matrix4x4.CreateFromQuaternion(q.LengthSquared() > 0f ? Quaternion.Normalize(q) : Quaternion.Identity);
        }
        else
        {
            rotation = BoneManifestEntry.EulerXyzToMatrix(rotate.X, rotate.Y, rotate.Z);
        }
        return new BoneLocal(scale, rotation, translate);
    }

    public static float EvaluateCurve(AnimCurveManifestEntry curve, float frame) =>
        AnimCurveEval.EvaluateFloat(curve.CurveType, curve.StartFrame, curve.EndFrame,
            curve.Scale, curve.Offset, curve.Frames, curve.Keys, frame);
}
