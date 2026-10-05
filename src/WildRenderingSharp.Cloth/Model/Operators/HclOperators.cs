using System;
using System.Numerics;

namespace WildRenderingSharp.Cloth.Model.Operators;

public abstract class HclOperator
{
    public string Name { get; set; } = string.Empty;
    public uint OperatorId { get; set; }
    public uint Type { get; set; }
}

public sealed class HclSimulateOperator : HclOperator
{
    public uint SimClothIndex { get; set; }

    /// <summary>
    /// Real <c>hclSimulateOperator::Config::m_constraintExecution</c>: the explicit order in which
    /// constraint sets are applied within one solve iteration. An entry is an index into the sim
    /// cloth's own constraint-set array, or <c>-1</c> meaning "run collision here". A set may appear
    /// MORE THAN ONCE (Zelda's data authors orders like <c>[2,0,0,0,4,5,1,-1,3]</c>, applying set 0
    /// three times), and sets may be scheduled AFTER the collision pass. When this is non-empty the
    /// real engine runs <c>_solveTaskOrderedCpu</c> instead of <c>_solveTaskCpu</c>; applying each
    /// set exactly once in array order with collision last, as this project used to, is a different
    /// solve altogether and converges far less well.
    /// </summary>
    public int[] ConstraintExecution { get; set; } = Array.Empty<int>();
    public byte SubSteps { get; set; } = 1;
    public byte NumberOfSolveIterations { get; set; } = 1;
    public bool UseAllInstanceCollidables { get; set; } = true;
    public bool[] InstanceCollidablesUsed { get; set; } = Array.Empty<bool>();
    public bool AdaptConstraintStiffness { get; set; }
}

public readonly struct VertexParticlePair
{
    public readonly ushort VertexIndex;
    public readonly ushort ParticleIndex;

    public VertexParticlePair(ushort vertexIndex, ushort particleIndex)
    {
        VertexIndex = vertexIndex;
        ParticleIndex = particleIndex;
    }
}

public sealed class HclMoveParticlesOperator : HclOperator
{
    public VertexParticlePair[] VertexParticlePairs { get; set; } = Array.Empty<VertexParticlePair>();
    public uint SimClothIndex { get; set; }
    public uint RefBufferIndex { get; set; }
}

public readonly struct TriangleBonePair
{
    public readonly ushort BoneOffset;
    public readonly ushort TriangleOffset;

    public TriangleBonePair(ushort boneOffset, ushort triangleOffset)
    {
        BoneOffset = boneOffset;
        TriangleOffset = triangleOffset;
    }
}

public sealed class HclSimpleMeshBoneDeformOperator : HclOperator
{
    public uint InputBufferIndex { get; set; }
    public uint OutputTransformSetIndex { get; set; }
    public TriangleBonePair[] TriangleBonePairs { get; set; } = Array.Empty<TriangleBonePair>();
    public Matrix4x4[] LocalBoneTransforms { get; set; } = Array.Empty<Matrix4x4>();
    public uint BoneAxis { get; set; }
}

public sealed class HclObjectSpaceSkinOperator : HclOperator
{
    public Matrix4x4[] BoneFromSkinMeshTransforms { get; set; } = Array.Empty<Matrix4x4>();
    public ushort[] TransformSubset { get; set; } = Array.Empty<ushort>();
    public uint OutputBufferIndex { get; set; }
    public uint TransformSetIndex { get; set; }
    public HclObjectSpaceDeformer Deformer { get; set; } = new();
}

/// <summary>
/// Position portion of Havok's <c>hclObjectSpaceDeformer</c>. The control stream selects one
/// independently-packed 16-vertex blend block and one matching local-position block at a time.
/// Bone indices address the skin operator's composite-matrix array (and therefore its transform
/// subset), not the actor skeleton directly.
/// </summary>
public sealed class HclObjectSpaceDeformer
{
    public HclObjectSpaceBlendBlock[][] BlendBlocks { get; set; } =
        Enumerable.Range(0, 8).Select(_ => Array.Empty<HclObjectSpaceBlendBlock>()).ToArray();
    public byte[] ControlBytes { get; set; } = Array.Empty<byte>();
    public Vector3[][] PackedLocalPositions { get; set; } = Array.Empty<Vector3[]>();
    public Vector3[][] UnpackedLocalPositions { get; set; } = Array.Empty<Vector3[]>();
    public ushort StartVertexIndex { get; set; }
    public ushort EndVertexIndex { get; set; }
    public bool PartialWrite { get; set; }
    public bool IsUsable => ControlBytes.Length > 0 &&
                            (PackedLocalPositions.Length >= ControlBytes.Count(c => c != 4) ||
                             UnpackedLocalPositions.Length >= ControlBytes.Count(c => c != 4));
}

public sealed class HclObjectSpaceBlendBlock
{
    public int InfluenceCount { get; set; }
    public ushort[] VertexIndices { get; set; } = Array.Empty<ushort>();
    public ushort[] BoneIndices { get; set; } = Array.Empty<ushort>();
    public float[] BoneWeights { get; set; } = Array.Empty<float>();
}

/// <summary>
/// Real <c>hclGatherAllVerticesOperator</c>. Copies vertices from one buffer to another through
/// <see cref="VertexInputFromVertexOutput"/>: for each OUTPUT vertex, the INPUT vertex to take it
/// from, or -1 for "leave alone". This is the authoritative statement of how a cloth piece's
/// buffers relate to each other - in particular how simulated particle positions get back into the
/// reference buffer the skin/move/local-range machinery uses.
/// </summary>
public sealed class HclGatherAllVerticesOperator : HclOperator
{
    public short[] VertexInputFromVertexOutput { get; set; } = Array.Empty<short>();
    public uint InputBufferIndex { get; set; }
    public uint OutputBufferIndex { get; set; }
    public bool GatherNormals { get; set; }
    public bool PartialGather { get; set; }
}
