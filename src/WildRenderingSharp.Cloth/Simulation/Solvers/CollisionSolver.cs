using System.Numerics;
using WildRenderingSharp.Cloth.Model.Collidables;

namespace WildRenderingSharp.Cloth.Simulation.Solvers;

/// <summary>
/// CPU implementation of Havok Cloth's regular particle/collidable path. The structure mirrors
/// hclSimulateOperatorCpu.cpp, hclCollideAndSolveTaskCollect.cxx and hclContactHandler.h.
/// </summary>
public static class CollisionSolver
{
    private const float Epsilon = 1e-7f;

    public struct Frame
    {
        private readonly SimClothRuntime _runtime;
        private readonly HclCollidable[] _collidables;
        private readonly Matrix4x4[] _from;
        private readonly Matrix4x4[] _to;
        private readonly Matrix4x4[] _current;
        private readonly Vector3[] _linearVelocities;
        private readonly Vector3[] _angularVelocities;
        private readonly float _frameTime;
        private float _elapsed;

        internal Frame(SimClothRuntime runtime, HclCollidable[] collidables,
            ReadOnlySpan<Matrix4x4> skeletonTransforms, ReadOnlySpan<uint> transformIndices,
            ReadOnlySpan<Matrix4x4> transformOffsets, IReadOnlyList<Matrix4x4>? bindPose,
            float frameTime)
        {
            _runtime = runtime;
            _collidables = collidables;
            _frameTime = Math.Max(frameTime, Epsilon);
            int count = _collidables.Length;
            _from = runtime.CollidableFrameFrom;
            _to = runtime.CollidableFrameTo;
            _current = runtime.CurrentCollidableTransforms;
            _linearVelocities = runtime.CollidableLinearVelocities;
            _angularVelocities = runtime.CollidableAngularVelocities;

            for (int c = 0; c < count; c++)
            {
                Matrix4x4 target = Rigid(TargetTransform(c, skeletonTransforms, transformIndices, transformOffsets, bindPose));
                Matrix4x4 previous = Rigid(runtime.PreviousCollidableTransforms[c]);
                _from[c] = previous;
                _to[c] = target;
                _current[c] = previous;
                _linearVelocities[c] = (target.Translation - previous.Translation) / _frameTime;
                _angularVelocities[c] = AngularVelocity(previous, target, _frameTime);
            }
        }

        /// <summary>Advances driven collidables once, before a simulation substep, as Havok does.</summary>
        public void Advance(float substepTime)
        {
            _elapsed = Math.Min(_frameTime, _elapsed + substepTime);
            float t = _elapsed / _frameTime;
            for (int c = 0; c < _current.Length; c++)
                _current[c] = InterpolateRigid(_from[c], _to[c], t);
        }

        public void Solve(bool useAllInstanceCollidables, ReadOnlySpan<bool> instanceCollidablesUsed, float timestep)
        {
            for (int c = 0; c < _collidables.Length; c++)
            {
                if (!_collidables[c].Enabled) continue;
                if (!useAllInstanceCollidables &&
                    (c >= instanceCollidablesUsed.Length || !instanceCollidablesUsed[c]))
                    continue;

                HclShape? shape = _collidables[c].Shape;
                if (shape == null) continue;

                uint colliderBit = 1u << Math.Min(c, 30);
                for (int p = 0; p < _runtime.ParticleCount; p++)
                {
                    // Per-instance collidables use the authored per-particle collision mask. Its
                    // bits already exclude fixed particles and unrelated body capsules.
                    if (_runtime.Data.StaticCollisionMasks.Length == _runtime.ParticleCount)
                    {
                        if ((_runtime.Data.StaticCollisionMasks[p] & colliderBit) == 0) continue;
                    }
                    else if (_runtime.InvMasses[p] == 0f)
                    {
                        continue;
                    }

                    if (ClosestPoint(shape, _current[c], _runtime.Positions[p],
                            out Vector3 contactPoint, out Vector3 normal, out float signedDistance) &&
                        signedDistance - _runtime.Radii[p] < 0f)
                    {
                        SolveContact(p, c, contactPoint, normal, timestep);
                    }
                }
            }
        }

        public void Commit()
        {
            for (int c = 0; c < _to.Length; c++)
                _runtime.PreviousCollidableTransforms[c] = _to[c];
        }

        private Matrix4x4 TargetTransform(int c, ReadOnlySpan<Matrix4x4> skeletonTransforms,
            ReadOnlySpan<uint> transformIndices, ReadOnlySpan<Matrix4x4> transformOffsets,
            IReadOnlyList<Matrix4x4>? bindPose)
        {
            Matrix4x4 target = _collidables[c].Transform;
            if (_runtime.Data.CollidableTransformSetIndex < 0) return target;
            int bone = c < transformIndices.Length ? (int)transformIndices[c] : -1;
            if ((uint)bone >= (uint)skeletonTransforms.Length) return target;

            if (c < transformOffsets.Length)
                return transformOffsets[c] * skeletonTransforms[bone];

            if (bindPose != null && bone < bindPose.Count &&
                Matrix4x4.Invert(bindPose[bone], out Matrix4x4 inverseBind))
                return target * inverseBind * skeletonTransforms[bone];

            return target;
        }

        private void SolveContact(int particle, int collider, Vector3 contactPoint, Vector3 normal, float timestep)
        {
            // hclRegularContactHandler::_solveContact, term for term. Friction is applied at its
            // authored strength and in the moving collider's rest frame.
            Vector3 p = _runtime.Positions[particle];
            Vector3 previous = _runtime.PrevPositions[particle];
            float radius = _runtime.Radii[particle];

            Vector3 newP = p + normal * (radius - Vector3.Dot(p - contactPoint, normal));
            Vector3 relativeContact = contactPoint - _current[collider].Translation;
            Vector3 contactVelocity = _linearVelocities[collider] +
                                      Vector3.Cross(_angularVelocities[collider], relativeContact);
            Vector3 tangential = newP - previous - timestep * contactVelocity;
            tangential -= normal * Vector3.Dot(tangential, normal);

            previous += _runtime.Frictions[particle] * tangential;
            _runtime.Positions[particle] = newP;
            _runtime.PrevPositions[particle] = previous;
        }
    }

    public static Frame BeginFrame(SimClothRuntime runtime, HclCollidable[] collidables,
        ReadOnlySpan<Matrix4x4> skeletonTransforms, ReadOnlySpan<uint> transformIndices,
        ReadOnlySpan<Matrix4x4> transformOffsets, IReadOnlyList<Matrix4x4>? bindPose, float frameTime)
        => new(runtime, collidables, skeletonTransforms, transformIndices, transformOffsets, bindPose, frameTime);

    private static bool ClosestPoint(HclShape shape, in Matrix4x4 transform, Vector3 p,
        out Vector3 pointOnSurface, out Vector3 normal, out float signedDistance)
    {
        switch (shape)
        {
            case HclCapsuleShape capsule:
                return CapsuleClosestPoint(capsule, transform, p, out pointOnSurface, out normal, out signedDistance);
            case HclSphereShape sphere:
                return SphereClosestPoint(Vector3.Transform(sphere.Center, transform), sphere.Radius, p,
                    out pointOnSurface, out normal, out signedDistance);
            case HclTaperedCapsuleShape tapered:
                return TaperedCapsuleClosestPoint(tapered, transform, p,
                    out pointOnSurface, out normal, out signedDistance);
            case HclPlaneShape plane:
                return PlaneClosestPoint(plane, transform, p, out pointOnSurface, out normal, out signedDistance);
            default:
                pointOnSurface = normal = default;
                signedDistance = float.PositiveInfinity;
                return false;
        }
    }

    private static bool CapsuleClosestPoint(HclCapsuleShape capsule, in Matrix4x4 transform, Vector3 p,
        out Vector3 surface, out Vector3 normal, out float distance)
    {
        Vector3 a = Vector3.Transform(capsule.Start, transform);
        Vector3 b = Vector3.Transform(capsule.End, transform);
        Vector3 axis = b - a;
        float denom = axis.LengthSquared();
        float t = denom > Epsilon ? Math.Clamp(Vector3.Dot(p - a, axis) / denom, 0f, 1f) : 0f;
        Vector3 linePoint = a + axis * t;
        Vector3 delta = p - linePoint;
        float length = delta.Length();
        normal = length > Epsilon ? delta / length : PerpendicularTo(axis);
        surface = linePoint + normal * capsule.Radius;
        distance = length - capsule.Radius;
        return true;
    }

    private static bool SphereClosestPoint(Vector3 center, float radius, Vector3 p,
        out Vector3 surface, out Vector3 normal, out float distance)
    {
        Vector3 delta = p - center;
        float length = delta.Length();
        normal = length > Epsilon ? delta / length : Vector3.UnitY;
        surface = center + normal * radius;
        distance = length - radius;
        return true;
    }

    private static bool TaperedCapsuleClosestPoint(HclTaperedCapsuleShape tapered,
        in Matrix4x4 transform, Vector3 p, out Vector3 surface, out Vector3 normal, out float distance)
    {
        // Exact SDK geometry: a truncated cone tangent to two spherical caps.
        Vector3 small = Vector3.Transform(tapered.Small, transform);
        Vector3 big = Vector3.Transform(tapered.Big, transform);
        float r = tapered.SmallRadius;
        float radiusBig = tapered.BigRadius;
        if (r > radiusBig)
        {
            (small, big) = (big, small);
            (r, radiusBig) = (radiusBig, r);
        }

        Vector3 coneAxis = big - small;
        float length = coneAxis.Length();
        float radiusDelta = radiusBig - r;
        if (length <= Epsilon || radiusDelta <= Epsilon || radiusDelta >= length)
            return CapsuleClosestPoint(
                new HclCapsuleShape { Start = tapered.Small, End = tapered.Big, Radius = MathF.Max(r, radiusBig) },
                transform, p, out surface, out normal, out distance);

        coneAxis /= length;
        float sinTheta = radiusDelta / length;
        float cosTheta = MathF.Sqrt(MathF.Max(Epsilon, 1f - sinTheta * sinTheta));
        float tanTheta = sinTheta / cosTheta;
        float d = r / sinTheta;
        Vector3 apex = small - d * coneAxis;

        Vector3 fromApex = p - apex;
        float z = Vector3.Dot(fromApex, coneAxis);
        Vector3 perpendicular = fromApex - z * coneAxis;
        float perpendicularLength = perpendicular.Length();
        Vector3 perpendicularDirection = perpendicularLength > Epsilon
            ? perpendicular / perpendicularLength
            : PerpendicularTo(coneAxis);
        float coneBand = d - tanTheta * perpendicularLength;

        if (z <= coneBand)
            return SphereClosestPoint(small, r, p, out surface, out normal, out distance);
        if (z >= length + coneBand)
            return SphereClosestPoint(big, radiusBig, p, out surface, out normal, out distance);

        distance = (perpendicularLength - tanTheta * z) * cosTheta;
        normal = Vector3.Normalize(cosTheta * perpendicularDirection - sinTheta * coneAxis);
        surface = p - distance * normal;
        return true;
    }

    private static bool PlaneClosestPoint(HclPlaneShape plane, in Matrix4x4 transform, Vector3 p,
        out Vector3 surface, out Vector3 normal, out float distance)
    {
        Vector4 eq = plane.PlaneEquation;
        normal = Vector3.Normalize(Vector3.TransformNormal(new Vector3(eq.X, eq.Y, eq.Z), transform));
        float transformedD = eq.W - Vector3.Dot(normal, transform.Translation);
        distance = Vector3.Dot(normal, p) + transformedD;
        surface = p - distance * normal;
        return true;
    }

    private static Matrix4x4 Rigid(in Matrix4x4 matrix)
    {
        if (!Matrix4x4.Decompose(matrix, out _, out Quaternion rotation, out Vector3 translation))
            return Matrix4x4.CreateTranslation(matrix.Translation);
        return Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation)) *
               Matrix4x4.CreateTranslation(translation);
    }

    private static Matrix4x4 InterpolateRigid(in Matrix4x4 from, in Matrix4x4 to, float t)
    {
        Matrix4x4.Decompose(from, out _, out Quaternion q0, out Vector3 p0);
        Matrix4x4.Decompose(to, out _, out Quaternion q1, out Vector3 p1);
        if (Quaternion.Dot(q0, q1) < 0f) q1 = -q1;
        return Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(q0, q1, t)) *
               Matrix4x4.CreateTranslation(Vector3.Lerp(p0, p1, t));
    }

    private static Vector3 AngularVelocity(in Matrix4x4 from, in Matrix4x4 to, float time)
    {
        Matrix4x4.Decompose(from, out _, out Quaternion q0, out _);
        Matrix4x4.Decompose(to, out _, out Quaternion q1, out _);
        if (Quaternion.Dot(q0, q1) < 0f) q1 = -q1;
        Quaternion delta = Quaternion.Normalize(Quaternion.Multiply(q1, Quaternion.Inverse(q0)));
        float angle = 2f * MathF.Acos(Math.Clamp(delta.W, -1f, 1f));
        float sinHalf = MathF.Sqrt(MathF.Max(0f, 1f - delta.W * delta.W));
        if (angle <= Epsilon || sinHalf <= Epsilon) return Vector3.Zero;
        return new Vector3(delta.X, delta.Y, delta.Z) * (angle / (sinHalf * time));
    }

    private static Vector3 PerpendicularTo(Vector3 axis)
    {
        if (axis.LengthSquared() <= Epsilon) return Vector3.UnitY;
        Vector3 n = Vector3.Normalize(axis);
        Vector3 basis = MathF.Abs(n.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
        return Vector3.Normalize(Vector3.Cross(n, basis));
    }
}
