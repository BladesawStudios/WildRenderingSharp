using System.Numerics;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Rendering;

/// <summary>
/// Walks a <see cref="SkeletonManifest"/>'s hierarchy into per-bone WORLD matrices - at bind pose,
/// or (given a <see cref="SkeletalAnimManifest"/> and a frame) at an animated pose.
///
/// THE WALK (<see cref="World"/>) reproduces <c>nn::g3d2::SkeletonObj::CalculateWorldImpl</c>,
/// whose three specialisations <c>CalculateWorldMtx</c> (Ghidra 0x710008246c) selects with
/// <c>(ResSkeleton.flags &gt;&gt; 8) &amp; 3</c> - <see cref="SkeletonManifest.ScalingMode"/>:
///
///   <see cref="SkeletonScalingMode.None"/> (0x7100080b40) - identical to Standard except the local
///     scale is never applied at all.
///   <see cref="SkeletonScalingMode.Standard"/> (0x7100080d00) -
///     <c>world = Scale * Rotate * Translate * parentWorld</c> in row-vector order. The game does
///     the scale last, multiplying world rows 0/1/2 by scale.x/y/z AFTER the parent multiply and
///     leaving the translation row alone, which is the same matrix as putting Scale first here.
///   <see cref="SkeletonScalingMode.Maya"/> (0x7100080f2c) - Standard, plus SEGMENT SCALE
///     COMPENSATE: a bone whose flag bit 23 is set first divides its parent's world basis rows by
///     the PARENT'S OWN LOCAL scale, so the parent's scale does not cascade into it. This is the
///     one that turns a legitimately non-uniform rig into a mess of stretched limbs when it is
///     skipped, since the compensation is exactly what keeps a scaled parent from doubling up on
///     its children. (The game skips the divide when the parent's scale is already 1 - a pure
///     fast path, reproduced here only because a zero scale component must not divide.)
///   <see cref="SkeletonScalingMode.Softimage"/> has no specialisation in the shipped binary and is
///     treated as Maya.
///
/// THE CURVE MATH (<see cref="EvaluateCurve"/>) is likewise reverse engineered, not guessed:
/// <c>nn::g3d2::ResAnimCurve::EvaluateCubic&lt;float&gt;</c> (0x71000733b4),
/// <c>EvaluateLinear&lt;float&gt;</c> (0x71000736d0), <c>EvaluateBakedFloat&lt;float&gt;</c>
/// (0x7100073984) and <c>FindFrame&lt;float&gt;</c> (0x710007316c). Cubic keys are already-baked
/// polynomial coefficients (NOT explicit Hermite value/tangent pairs): with
/// <c>t = (frame - Frames[i]) / (Frames[i+1] - Frames[i])</c>,
/// <c>raw = Keys[i][0] + Keys[i][1]*t + Keys[i][2]*t^2 + Keys[i][3]*t^3</c>. Linear is
/// <c>raw = Keys[i][0] + Keys[i][1]*t</c> (same normalized t, so <c>Keys[i][1]</c> is the whole
/// segment's delta, not a per-frame slope). BakedFloat has no explicit frame list - one value per
/// integer frame, linearly blended by the fractional part. Every curve type finishes with
/// <c>value = Offset + raw * Scale</c> (<c>EvaluateFloat</c>, 0x7100073d90, reading
/// <c>ResAnimCurve[0x24]</c> and <c>[0x20]</c>) - Offset, not the separate Delta field at 0x28 that
/// only relative-repeat wrapping uses.
/// </summary>
public static class SkeletonPose
{
    /// <summary>A bone's resolved local transform for one frame - the inputs <see cref="World"/> composes. <see cref="Scale"/> is kept separately (rather than folded into <see cref="Rotation"/>) because Maya's segment scale compensate needs a PARENT'S local scale on its own.</summary>
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
    /// Same walk, but every bone with a matching <see cref="BoneAnimManifestEntry"/> gets its
    /// local TRS overridden by the anim's base values plus whatever curves it carries, evaluated
    /// at <paramref name="frame"/>. A bone the anim doesn't touch keeps its bind pose exactly.
    /// </summary>
    public static Matrix4x4[] AnimatedWorldMatrices(SkeletonManifest skel, SkeletalAnimManifest anim, float frame)
    {
        var byName = new Dictionary<string, BoneAnimManifestEntry>(anim.BoneAnims.Count, StringComparer.Ordinal);
        foreach (var ba in anim.BoneAnims)
            byName[ba.BoneName] = ba;

        var locals = new BoneLocal[skel.Bones.Count];
        // An animated bone's segment-scale-compensate state comes from the ANIM, not the skeleton:
        // ApplyToImpl copies the anim result's flag bits 23-27 straight over the bone's own.
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
    /// Composes per-bone local transforms into world matrices down the hierarchy, honouring
    /// <see cref="SkeletonManifest.ScalingMode"/>. Parents always precede children in a BFRES
    /// skeleton, so one forward pass suffices - a bone whose parent does not precede it is treated
    /// as a root rather than read out of order.
    /// </summary>
    /// <param name="sscOverride">Per-bone segment-scale-compensate flags to use instead of the skeleton's own (an anim can override them); null to use <see cref="BoneManifestEntry.SegmentScaleCompensate"/>.</param>
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
    /// Divides a world matrix's three basis rows by <paramref name="scale"/>.x/y/z, leaving its
    /// translation row alone - the segment-scale-compensate step
    /// <c>CalculateWorldImpl&lt;CalculateWorldMaya&gt;</c> applies to a bone's parent before
    /// composing (it reciprocates the parent's local scale and scales the parent's world rows).
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

        // Rotation lives in whichever representation the ANIM declares, so a curve-less bone's bind
        // rotation has to be expressed in that same representation before curves overwrite parts of
        // it. The two agree for every real file (an FSKA is authored against its skeleton), but
        // converting rather than assuming keeps a mismatch from silently producing garbage.
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

    /// <summary>Delegates to <see cref="AnimCurveEval"/>, which every kind of animation WildRenderingSharp plays shares - the maths and its Ghidra derivation live there rather than being repeated per animation type.</summary>
    public static float EvaluateCurve(AnimCurveManifestEntry curve, float frame) =>
        AnimCurveEval.EvaluateFloat(curve.CurveType, curve.StartFrame, curve.EndFrame,
            curve.Scale, curve.Offset, curve.Frames, curve.Keys, frame);
}
