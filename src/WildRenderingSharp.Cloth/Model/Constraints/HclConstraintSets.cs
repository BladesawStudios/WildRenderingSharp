namespace WildRenderingSharp.Cloth.Model.Constraints;

public abstract class HclConstraintSet
{
    public string Name { get; set; } = string.Empty;
    public uint ConstraintId { get; set; }
    public uint Type { get; set; }
}

public readonly struct StandardLink
{
    public readonly ushort ParticleA;
    public readonly ushort ParticleB;
    public readonly float RestLength;
    public readonly float Stiffness;

    public StandardLink(ushort particleA, ushort particleB, float restLength, float stiffness)
    {
        ParticleA = particleA;
        ParticleB = particleB;
        RestLength = restLength;
        Stiffness = stiffness;
    }
}

/// <summary>
/// A constraint set this project does not implement, kept ONLY so that it still occupies its slot
/// in the sim cloth's constraint-set array. <c>hclSimulateOperator::Config::m_constraintExecution</c>
/// schedules constraint sets BY INDEX into that array (see
/// <c>HclSimulateOperator.ConstraintExecution</c>), so dropping an unparsed set - every real piece
/// here carries an <c>hclTransitionConstraintSet</c> named "Transition" at index 3 - silently shifts
/// every later index and makes the authored solve order address the wrong constraints entirely.
/// The solver ignores this type; it exists purely to keep the indices honest.
/// </summary>
public sealed class HclUnsupportedConstraintSet : HclConstraintSet
{
}

public sealed class HclStandardLinkConstraintSet : HclConstraintSet
{
    public StandardLink[] Links { get; set; } = System.Array.Empty<StandardLink>();
}

public sealed class HclStretchLinkConstraintSet : HclConstraintSet
{
    public StandardLink[] Links { get; set; } = System.Array.Empty<StandardLink>();
}

public readonly struct CompressibleLink
{
    public readonly ushort ParticleA;
    public readonly ushort ParticleB;
    public readonly float RestLength;
    public readonly float CompressionLength;
    public readonly float Stiffness;

    public CompressibleLink(ushort particleA, ushort particleB, float restLength, float compressionLength, float stiffness)
    {
        ParticleA = particleA;
        ParticleB = particleB;
        RestLength = restLength;
        CompressionLength = compressionLength;
        Stiffness = stiffness;
    }
}

public sealed class HclCompressibleLinkConstraintSet : HclConstraintSet
{
    public CompressibleLink[] Links { get; set; } = System.Array.Empty<CompressibleLink>();
}

/// <summary>
/// Matches the real <c>hclBendLinkConstraintSet::Link</c> exactly (field names and order, per the
/// real Havok Cloth SDK source) - a prior version of this struct had the right byte offsets but
/// the WRONG field names for the last three (labelled `MaxDistance`/`StretchMaxLength`/`Stiffness`
/// where the real fields are `StretchMaxLength`/`BendStiffness`/`StretchStiffness` respectively),
/// which fed <see cref="WildRenderingSharp.Cloth.Simulation.Solvers.PbdSolver.SolveBendLinks"/> a single
/// conflated "stiffness" where the real constraint has two independent ones for its two unilateral
/// directions (resisting bending vs. resisting stretching).
/// </summary>
public readonly struct BendLink
{
    public readonly ushort ParticleA;
    public readonly ushort ParticleB;

    /// <summary>The link acts (pushing apart) once particles are closer than this - counteracts bending.</summary>
    public readonly float BendMinLength;

    /// <summary>The link acts (pulling together) once particles are further than this - counteracts stretching.</summary>
    public readonly float StretchMaxLength;

    /// <summary>Stiffness applied when resisting bending (compression below <see cref="BendMinLength"/>).</summary>
    public readonly float BendStiffness;

    /// <summary>Stiffness applied when resisting stretching (extension beyond <see cref="StretchMaxLength"/>).</summary>
    public readonly float StretchStiffness;

    public BendLink(ushort particleA, ushort particleB, float bendMinLength, float stretchMaxLength, float bendStiffness, float stretchStiffness)
    {
        ParticleA = particleA;
        ParticleB = particleB;
        BendMinLength = bendMinLength;
        StretchMaxLength = stretchMaxLength;
        BendStiffness = bendStiffness;
        StretchStiffness = stretchStiffness;
    }
}

public sealed class HclBendLinkConstraintSet : HclConstraintSet
{
    public BendLink[] Links { get; set; } = System.Array.Empty<BendLink>();
}

public readonly struct BendStiffnessLink
{
    public readonly float WeightA;
    public readonly float WeightB;
    public readonly float WeightC;
    public readonly float WeightD;
    public readonly float BendStiffness;
    public readonly float RestCurvature;
    public readonly ushort ParticleA;
    public readonly ushort ParticleB;
    public readonly ushort ParticleC;
    public readonly ushort ParticleD;

    public BendStiffnessLink(
        float weightA, float weightB, float weightC, float weightD,
        float bendStiffness, float restCurvature,
        ushort particleA, ushort particleB, ushort particleC, ushort particleD)
    {
        WeightA = weightA;
        WeightB = weightB;
        WeightC = weightC;
        WeightD = weightD;
        BendStiffness = bendStiffness;
        RestCurvature = restCurvature;
        ParticleA = particleA;
        ParticleB = particleB;
        ParticleC = particleC;
        ParticleD = particleD;
    }
}

public sealed class HclBendStiffnessConstraintSet : HclConstraintSet
{
    public BendStiffnessLink[] Links { get; set; } = System.Array.Empty<BendStiffnessLink>();
    public float MaxRestPoseHeightSq { get; set; }
    public bool ClampBendStiffness { get; set; }
    public bool UseRestPoseConfig { get; set; }
}

public readonly struct LocalRangeConstraint
{
    public readonly ushort ParticleIndex;
    public readonly ushort ReferenceVertex;

    /// <summary>Real <c>m_shapeRadius</c> - how far this particle may sit from its reference vertex.</summary>
    public readonly float MaxDistance;
    public readonly float MaxNormalDistance;
    public readonly float MinNormalDistance;

    /// <summary>
    /// Per-constraint stiffness. Real Havok stores local range constraints in one of two arrays:
    /// <c>m_localConstraints</c> (<c>hclLocalRangeConstraintSet::LocalConstraint</c>), whose own
    /// <c>getStiffness()</c> is hardcoded to return <c>1.0f</c>, or
    /// <c>m_localStiffnessConstraints</c> (<c>LocalStiffnessConstraint</c>), which carries a real
    /// per-constraint value - see <see cref="HclLocalRangeConstraintSet.LocalConstraints"/>.
    /// </summary>
    public readonly float Stiffness;

    public LocalRangeConstraint(ushort particleIndex, ushort referenceVertex, float maxDistance, float maxNormalDistance, float minNormalDistance, float stiffness = 1.0f)
    {
        ParticleIndex = particleIndex;
        ReferenceVertex = referenceVertex;
        MaxDistance = maxDistance;
        MaxNormalDistance = maxNormalDistance;
        MinNormalDistance = minNormalDistance;
        Stiffness = stiffness;
    }
}

public sealed class HclLocalRangeConstraintSet : HclConstraintSet
{
    /// <summary>
    /// The real class holds FOUR separate constraint arrays - <c>m_localConstraints</c>,
    /// <c>m_localStiffnessConstraints</c>, <c>m_localCapsuleConstraints</c> and
    /// <c>m_localCapsuleStiffnessConstraints</c> - of which the CPU solver uses exactly one, picked
    /// by <c>m_shapeType</c> plus a plain "first non-empty of the two" test
    /// (<c>hclLocalRangeConstraintSetCpu.cpp</c>: <c>if (!m_localConstraints.isEmpty()) ... else if
    /// (!m_localStiffnessConstraints.isEmpty()) ...</c>). This project used to read only the first
    /// array, so every piece whose constraints live in the stiffness variant silently parsed as
    /// having NO local range constraints at all - which was the case for all three of Zelda's back
    /// hair pieces (62 tethers dropped between them). Both sphere variants are merged into this one
    /// list, carrying their own per-constraint stiffness.
    /// </summary>
    public LocalRangeConstraint[] LocalConstraints { get; set; } = System.Array.Empty<LocalRangeConstraint>();
    public uint ReferenceMeshBufferIdx { get; set; }

    /// <summary>Set-level stiffness, multiplied with each constraint's own (real: <c>lrStiffness = m_stiffness * constraintStiffness</c>, then <c>localConstraint.getStiffness() * lrStiffness</c>).</summary>
    public float Stiffness { get; set; } = 1.0f;

    /// <summary>Real <c>m_shapeType</c>: 0 = SPHERE, 1 = CYLINDER, 2 = CAPSULE. Only SPHERE is implemented by this project's solver (every real cloth piece checked so far authors 0).</summary>
    public uint ShapeType { get; set; }
}
