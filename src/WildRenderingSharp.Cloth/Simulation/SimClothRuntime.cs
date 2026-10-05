using System;
using System.Collections.Generic;
using System.Numerics;
using WildRenderingSharp.Cloth.Model;

namespace WildRenderingSharp.Cloth.Simulation;

/// <summary>
/// Holds the runtime mutable state for a single simulated cloth mesh (hclSimClothData).
/// </summary>
public sealed class SimClothRuntime
{
    public HclSimClothData Data { get; }

    public int ParticleCount => Positions.Length;
    public Vector3[] Positions { get; }
    public Vector3[] PrevPositions { get; }
    public Vector3[] Velocities { get; }
    public float[] InvMasses { get; }
    public float[] Radii { get; }
    public float[] Frictions { get; }
    public bool[] IsFixed { get; }

    // End-of-previous-frame poses for Havok's implicitSet -> per-substep collider motion path.
    // Kept per sim-cloth runtime because each piece owns independent collidable instances.
    internal Matrix4x4[] PreviousCollidableTransforms { get; }
    internal Matrix4x4[] CollidableFrameFrom { get; }
    internal Matrix4x4[] CollidableFrameTo { get; }
    internal Matrix4x4[] CurrentCollidableTransforms { get; }
    internal Vector3[] CollidableLinearVelocities { get; }
    internal Vector3[] CollidableAngularVelocities { get; }

    public Vector3 Gravity { get; set; }
    public float GlobalDamping { get; set; }

    public SimClothRuntime(HclSimClothData data)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));

        int count = data.Particles.Length;
        Positions = new Vector3[count];
        PrevPositions = new Vector3[count];
        Velocities = new Vector3[count];
        InvMasses = new float[count];
        Radii = new float[count];
        Frictions = new float[count];
        IsFixed = new bool[count];
        int collidableCount = data.Collidables.Length;
        PreviousCollidableTransforms = new Matrix4x4[collidableCount];
        CollidableFrameFrom = new Matrix4x4[collidableCount];
        CollidableFrameTo = new Matrix4x4[collidableCount];
        CurrentCollidableTransforms = new Matrix4x4[collidableCount];
        CollidableLinearVelocities = new Vector3[collidableCount];
        CollidableAngularVelocities = new Vector3[collidableCount];

        Gravity = data.Gravity;
        GlobalDamping = data.GlobalDamping;

        // Initialize particle properties
        for (int i = 0; i < count; i++)
        {
            var p = data.Particles[i];
            InvMasses[i] = p.InvMass;
            Radii[i] = p.Radius;
            Frictions[i] = p.Friction;
        }

        // Mark fixed particles
        if (data.FixedParticles != null)
        {
            foreach (ushort fixedIdx in data.FixedParticles)
            {
                if (fixedIdx < count)
                {
                    IsFixed[fixedIdx] = true;
                    InvMasses[fixedIdx] = 0f; // Infinite mass for fixed particles
                }
            }
        }

        // Initialize positions from first pose if available
        ResetToPose(0);
    }

    public void ResetToPose(int poseIndex = 0)
    {
        for (int c = 0; c < PreviousCollidableTransforms.Length; c++)
            PreviousCollidableTransforms[c] = Data.Collidables[c].Transform;
        if (Data.Poses.Length > 0 && poseIndex >= 0 && poseIndex < Data.Poses.Length)
        {
            var pose = Data.Poses[poseIndex];
            int copyCount = Math.Min(pose.Positions.Length, Positions.Length);
            for (int i = 0; i < copyCount; i++)
            {
                Positions[i] = pose.Positions[i];
                PrevPositions[i] = pose.Positions[i];
                Velocities[i] = Vector3.Zero;
            }
        }
    }

    /// <summary>
    /// Moves an anchor particle onto its newly-skinned position, with the real
    /// <c>hclMoveParticlesOperator</c>'s two distinct semantics.
    ///
    /// <para><paramref name="fixVelocity"/> is the real operator's own
    /// <c>m_fixedParticles.getSize() == 0</c> test. When there IS no fixed-particle list, Havok
    /// slams both the current and previous Verlet positions onto the skinned point, giving the
    /// anchor an identically zero velocity. When there IS one - which is the case for every real
    /// cloth piece this project loads - it instead shifts <c>previous := current</c> BEFORE
    /// overwriting current, deliberately preserving how far the anchor travelled this frame. That
    /// retained displacement is not decoration: <see cref="ClothInstance"/>'s simulate step consumes
    /// it to interpolate anchors across sub-steps (see the real
    /// <c>hclSimulateOperator::executeCpu</c>'s <c>ParticleInterpolation</c> buffer), so destroying
    /// it - which this method unconditionally used to do - leaves the anchor able only to teleport.
    /// </para>
    /// </summary>
    public void MoveParticleTo(int particleIndex, Vector3 targetPosition, bool fixVelocity)
    {
        if ((uint)particleIndex >= (uint)Positions.Length) return;

        if (fixVelocity)
        {
            Positions[particleIndex] = targetPosition;
            PrevPositions[particleIndex] = targetPosition;
        }
        else
        {
            PrevPositions[particleIndex] = Positions[particleIndex];
            Positions[particleIndex] = targetPosition;
        }

        Velocities[particleIndex] = Vector3.Zero;
    }
}
