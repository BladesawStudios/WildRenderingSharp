using System;
using System.Numerics;
using WildRenderingSharp.Cloth.Model.Collidables;
using WildRenderingSharp.Cloth.Model.Constraints;

namespace WildRenderingSharp.Cloth.Model;

public readonly struct HclParticleData
{
    public readonly float Mass;
    public readonly float InvMass;
    public readonly float Radius;
    public readonly float Friction;

    public HclParticleData(float mass, float invMass, float radius, float friction)
    {
        Mass = mass;
        InvMass = invMass;
        Radius = radius;
        Friction = friction;
    }
}

public sealed class HclSimClothPose
{
    public string Name { get; set; } = string.Empty;
    public Vector3[] Positions { get; set; } = Array.Empty<Vector3>();
}

public readonly record struct HclCollidablePinchingData(
    bool Enabled,
    sbyte Priority,
    float Radius);

public sealed class HclSimClothData
{
    public string Name { get; set; } = string.Empty;
    public Vector3 Gravity { get; set; } = new Vector3(0, -9.81f, 0);
    public float GlobalDamping { get; set; } = 0.1f;

    public HclParticleData[] Particles { get; set; } = Array.Empty<HclParticleData>();
    public ushort[] FixedParticles { get; set; } = Array.Empty<ushort>();
    public HclSimClothPose[] Poses { get; set; } = Array.Empty<HclSimClothPose>();
    public HclConstraintSet[] ConstraintSets { get; set; } = Array.Empty<HclConstraintSet>();
    public HclCollidable[] Collidables { get; set; } = Array.Empty<HclCollidable>();
    public int CollidableTransformSetIndex { get; set; } = -1;
    public uint[] CollidableTransformIndices { get; set; } = Array.Empty<uint>();
    public Matrix4x4[] CollidableTransformOffsets { get; set; } = Array.Empty<Matrix4x4>();
    /// <summary>One authored bit per per-instance collidable, for every particle.</summary>
    public uint[] StaticCollisionMasks { get; set; } = Array.Empty<uint>();
    public bool PinchDetectionEnabled { get; set; }
    public bool[] PerParticlePinchDetectionEnabled { get; set; } = Array.Empty<bool>();
    public HclCollidablePinchingData[] CollidablePinchingData { get; set; } = Array.Empty<HclCollidablePinchingData>();
    public ushort NumVirtualCollisionPoints { get; set; }

    public ushort[] TriangleIndices { get; set; } = Array.Empty<ushort>();
    public float TotalMass { get; set; }
}
