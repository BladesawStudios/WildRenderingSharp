using System;
using System.Numerics;
using WildRenderingSharp.Cloth.Model.Operators;

namespace WildRenderingSharp.Cloth.Simulation.Solvers;

/// <summary>
/// Implements hclSimpleMeshBoneDeformOperator: reconstructs skeleton bone transforms from simulated cloth particle triangles.
/// </summary>
public static class MeshBoneSolver
{
    public static void Execute(
        HclSimpleMeshBoneDeformOperator op,
        SimClothRuntime runtime,
        ReadOnlySpan<ushort> triangleIndices,
        Span<Matrix4x4> outputSkeletonTransforms)
    {
        var pos = runtime.Positions;
        var pairs = op.TriangleBonePairs;
        var localBones = op.LocalBoneTransforms;

        for (int i = 0; i < pairs.Length; i++)
        {
            ref readonly var pair = ref pairs[i];
            int triIdx = pair.TriangleOffset / 6; // 3 ushorts = 6 bytes per triangle
            int boneIdx = pair.BoneOffset / 64;   // 16 floats = 64 bytes per Matrix4x4

            if (boneIdx >= outputSkeletonTransforms.Length)
                continue;

            int i0Idx = triIdx * 3;
            if (i0Idx + 2 >= triangleIndices.Length)
                continue;

            int p0 = triangleIndices[i0Idx];
            int p1 = triangleIndices[i0Idx + 1];
            int p2 = triangleIndices[i0Idx + 2];

            if (p0 >= pos.Length || p1 >= pos.Length || p2 >= pos.Length)
                continue;

            Vector3 v0 = pos[p0];
            Vector3 v1 = pos[p1];
            Vector3 v2 = pos[p2];

            // hclMeshBoneDeformUtility::calculateTriangleTransform - the UNNORMALISED centroid
            // basis (cols in Havok's column-vector convention = rows here):
            //   centroid = (a+b+c)/3;  sAxis = a-centroid;  tAxis = b-centroid;
            //   nAxis = cross(sAxis, tAxis);  cols = (sAxis, tAxis, nAxis, centroid)
            // cross(a-centroid, b-centroid) is algebraically cross(b-a, c-a)/3, which is the form
            // used here.
            Vector3 c = (v0 + v1 + v2) / 3.0f;
            Vector3 n = Vector3.Cross(v1 - v0, v2 - v0) / 3.0f;

            var triFrame = new Matrix4x4(
                v0.X - c.X, v0.Y - c.Y, v0.Z - c.Z, 0f,
                v1.X - c.X, v1.Y - c.Y, v1.Z - c.Z, 0f,
                n.X,        n.Y,        n.Z,        0f,
                c.X,        c.Y,        c.Z,        1f
            );

            Matrix4x4 localBone = i < localBones.Length ? localBones[i] : Matrix4x4.Identity;

            // Real: deformedBoneXform.setMul(triangleTransform, localBoneTransform) in Havok's
            // column-vector convention, which is localBone * triFrame in this codebase's
            // row-vector one.
            Matrix4x4 deformedBone = localBone * triFrame;

            Reorthogonalize(ref deformedBone, op.BoneAxis);

            outputSkeletonTransforms[boneIdx] = deformedBone;
        }
    }

    /// <summary>
    /// The final step of the real <c>hclSimpleMeshBoneDeformOperator::executeCpu</c>, and the one
    /// this solver used to omit entirely: after composing the triangle basis with the local bone
    /// transform, Havok rebuilds two of the three basis axes with cross products and
    /// <b>unit-normalises all three</b> (<c>hclMeshBoneDeformUtility::BoneLength*Axis::reorthogonalize</c>),
    /// so a deformed bone is always a pure rotation plus translation with NO scale and NO shear.
    ///
    /// <para>Omitting this is catastrophic rather than merely inexact, because the basis being
    /// composed is deliberately unnormalised: <c>sAxis</c>/<c>tAxis</c> are on the order of the
    /// triangle's own edge length and <c>nAxis</c> is their CROSS PRODUCT, i.e. QUADRATIC in it.
    /// The authored <c>m_localBoneTransforms</c> entry is (inverse rest basis * rest bone), so it
    /// carries the reciprocals of those rest magnitudes - roughly 1/0.03 on two axes and 1/0.0006 on
    /// the normal for a ~4cm cloth triangle. At exactly the rest pose the two cancel to the identity
    /// scale, which is why a rest-pose-only check (<c>TestMeshBoneSolver</c>) passed for so long
    /// while this was missing. As soon as the triangle deforms, the leftover ratio rides straight
    /// into the bone matrix and gets applied to every vertex skinned to it - and on a small piece
    /// like a wrist cuff, whose particles are permitted several centimetres of local-range sway
    /// against ~4cm edges, the normal axis alone can grow by the SQUARE of that ratio. That is the
    /// reported "bracelets are as big as Zelda": not a solver instability, a missing normalisation.
    /// </para>
    ///
    /// <para>Which axis is preserved is authored per operator (<c>m_boneAxis</c>, default X_AXIS) -
    /// it names the bone's own length axis, which must keep pointing along the cloth so the bone
    /// doesn't spin; the other two are rebuilt around it in a specific order that differs per
    /// variant, reproduced exactly below.</para>
    /// </summary>
    private static void Reorthogonalize(ref Matrix4x4 m, uint boneAxis)
    {
        // Havok reads these as matrix COLUMNS; this codebase's row-vector matrices carry the same
        // basis vectors as ROWS. Row 3 (translation) is deliberately left untouched - the real code
        // re-writes `position` unmodified.
        Vector3 x = new(m.M11, m.M12, m.M13);
        Vector3 y = new(m.M21, m.M22, m.M23);
        Vector3 z = new(m.M31, m.M32, m.M33);

        switch (boneAxis)
        {
            case 1: // Y_AXIS: retains Y, recomputes Z first and then X
                z = Vector3.Cross(x, y);
                x = Vector3.Cross(y, z);
                break;
            case 2: // Z_AXIS: retains Z, recomputes X first and then Y
                x = Vector3.Cross(y, z);
                y = Vector3.Cross(z, x);
                break;
            case 3: // LEGACY (pre-2020.2): retains Z, recomputes Y first and then X
                y = Vector3.Cross(z, x);
                x = Vector3.Cross(y, z);
                break;
            default: // 0 - X_AXIS: retains X, recomputes Y first and then Z
                y = Vector3.Cross(z, x);
                z = Vector3.Cross(x, y);
                break;
        }

        // Havok normalises with SQRT_SET_ZERO, i.e. a degenerate (zero-length) axis becomes zero
        // rather than NaN - mirrored here so a collapsed triangle can never poison the bone palette.
        x = SafeNormalize(x);
        y = SafeNormalize(y);
        z = SafeNormalize(z);

        m.M11 = x.X; m.M12 = x.Y; m.M13 = x.Z;
        m.M21 = y.X; m.M22 = y.Y; m.M23 = y.Z;
        m.M31 = z.X; m.M32 = z.Y; m.M33 = z.Z;
    }

    private static Vector3 SafeNormalize(Vector3 v)
    {
        float len = v.Length();
        return len > 1e-12f ? v / len : Vector3.Zero;
    }
}
