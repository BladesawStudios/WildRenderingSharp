using System;
using System.Collections.Generic;
using System.Numerics;

namespace WildRenderingSharp.Cloth.Model.Animation;

public sealed class HkaBone
{
    public string Name { get; set; } = string.Empty;
    public short ParentIndex { get; set; }
}

public sealed class HkaSkeleton
{
    public string Name { get; set; } = string.Empty;
    public List<HkaBone> Bones { get; } = new();

    /// <summary>Per-bone LOCAL (parent-relative) bind transform, straight off hkaSkeleton::m_referencePose - NOT usable directly as an object-space bone matrix. Use <see cref="WorldReferencePose"/> for that.</summary>
    public List<Matrix4x4> ReferencePose { get; } = new();

    private List<Matrix4x4>? _worldReferencePose;

    /// <summary>
    /// Object-space bind pose, computed by walking <see cref="ReferencePose"/> up through
    /// <see cref="HkaBone.ParentIndex"/> (assumes the standard Havok invariant that a bone's parent
    /// always has a lower index than the bone itself). Matches the space <c>SkeletonTransforms</c>
    /// is fed in at runtime (the game skeleton's own accumulated world matrices), which is what
    /// makes rest-pose vertices comparable against a bone's current animated matrix at all.
    /// </summary>
    public List<Matrix4x4> WorldReferencePose
    {
        get
        {
            if (_worldReferencePose == null)
            {
                _worldReferencePose = new List<Matrix4x4>(ReferencePose.Count);
                for (int i = 0; i < ReferencePose.Count; i++)
                {
                    Matrix4x4 local = ReferencePose[i];
                    short parent = i < Bones.Count ? Bones[i].ParentIndex : (short)-1;
                    Matrix4x4 world = (parent >= 0 && parent < _worldReferencePose.Count)
                        ? local * _worldReferencePose[parent]
                        : local;
                    _worldReferencePose.Add(world);
                }
            }
            return _worldReferencePose;
        }
    }
}

public sealed class HkaAnimationContainer
{
    public List<HkaSkeleton> Skeletons { get; } = new();
}
