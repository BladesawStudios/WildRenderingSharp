using System.Numerics;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Rendering;

/// <summary>
/// Walks a <see cref="SkeletonManifest"/>'s hierarchy into per-bone world matrices, at bind pose or (given a <see
/// cref="SkeletalAnimManifest"/> and a frame) at an animated pose.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="World"/> reproduces <c>nn::g3d2::SkeletonObj::CalculateWorldImpl</c>, whose three specialisations <c>CalculateWorldMtx</c> (Ghidra 0x710008246c) selects with
/// <c>(ResSkeleton.flags &gt;&gt; 8) &amp; 3</c>, i.e. <see cref="SkeletonManifest.ScalingMode"/>. <see cref="SkeletonScalingMode.None"/> (0x7100080b40) never applies local scale.
/// <see cref="SkeletonScalingMode.Standard"/> (0x7100080d00) is <c>world = Scale * Rotate * Translate * parentWorld</c> in row-vector order; the game multiplies world rows 0/1/2 by
/// scale.x/y/z after the parent multiply and leaves the translation row alone, the same matrix as scaling first here. <see cref="SkeletonScalingMode.Maya"/> (0x7100080f2c) adds
/// segment scale compensate: a bone with flag bit 23 first divides its parent's world basis rows by the parent's own local scale, so the parent's scale does not cascade into it
/// (skipping it stretches a non-uniform rig; the game skips the divide when the scale is already 1, reproduced only because a zero component must not divide).
/// <see cref="SkeletonScalingMode.Softimage"/> has no specialisation and is treated as Maya.
/// </para>
/// <para>
/// <see cref="EvaluateCurve"/> is reverse engineered from <c>nn::g3d2::ResAnimCurve::EvaluateCubic&lt;float&gt;</c> (0x71000733b4), <c>EvaluateLinear</c> (0x71000736d0),
/// <c>EvaluateBakedFloat</c> (0x7100073984) and <c>FindFrame</c> (0x710007316c). Cubic keys are baked polynomial coefficients, not Hermite value and tangent pairs: with
/// <c>t = (frame - Frames[i]) / (Frames[i+1] - Frames[i])</c>, <c>raw = Keys[i][0] + Keys[i][1]*t + Keys[i][2]*t^2 + Keys[i][3]*t^3</c>. Linear is
/// <c>raw = Keys[i][0] + Keys[i][1]*t</c> with the same normalised t, so <c>Keys[i][1]</c> is the segment's whole delta. BakedFloat has no frame list: one value per integer
/// frame, blended by the fractional part. Every type finishes with <c>value = Offset + raw * Scale</c> (<c>EvaluateFloat</c>, 0x7100073d90, reading <c>ResAnimCurve[0x24]</c> and
/// <c>[0x20]</c>); Offset, not the Delta field at 0x28 that only relative-repeat wrapping uses.
/// </para>
/// </remarks>
public static class SkeletonPose
{
    /// <summary>
    /// A bone's resolved local transform for one frame - the inputs <see cref="World"/> composes. <see cref="Scale"/> is kept
    /// separately (rather than folded into <see cref="Rotation"/>) because Maya's segment scale compensate needs a PARENT'S local
    /// scale on its own.
    /// </summary>
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

    /// <summary>
    /// The same walk with every bone that has a matching <see cref="BoneAnimManifestEntry"/> getting its local TRS overridden by
    /// the anim's base values plus its curves at <paramref name="frame"/>. A bone the anim does not touch keeps its bind pose.
    /// </summary>
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

    /// <summary>
    /// Composes per-bone local transforms into world matrices down the hierarchy, honouring <see
    /// cref="SkeletonManifest.ScalingMode"/>. Parents precede children in a BFRES skeleton, so one forward pass suffices; a bone
    /// whose parent does not precede it is treated as a root.
    /// </summary>
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

    /// <summary>
    /// Divides a world matrix's three basis rows by <paramref name="scale"/>.x/y/z, leaving the translation row alone: the
    /// segment-scale-compensate step <c>CalculateWorldImpl&lt;CalculateWorldMaya&gt;</c> applies to a bone's parent before
    /// composing.
    /// </summary>
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

    /// <summary>
    /// Delegates to <see cref="AnimCurveEval"/>, shared by every kind of animation played; the maths and its Ghidra derivation live
    /// there.
    /// </summary>
    public static float EvaluateCurve(AnimCurveManifestEntry curve, float frame) =>
        AnimCurveEval.EvaluateFloat(curve.CurveType, curve.StartFrame, curve.EndFrame,
            curve.Scale, curve.Offset, curve.Frames, curve.Keys, frame);
}
