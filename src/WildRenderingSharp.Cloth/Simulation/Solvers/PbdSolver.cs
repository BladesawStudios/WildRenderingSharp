using System;
using System.Numerics;
using WildRenderingSharp.Cloth.Model.Constraints;

namespace WildRenderingSharp.Cloth.Simulation.Solvers;

/// <summary>
/// Position-Based Dynamics integrator and constraint projection solver.
///
/// Every constraint solve method below was rewritten against the REAL Havok Cloth SDK source
/// (Source/Cloth/Cloth/Constraint/&lt;Type&gt;/hcl&lt;Type&gt;ConstraintSet.cpp - the user had the
/// actual SDK checked out and this project read it directly), replacing two earlier, both-wrong
/// guesses:
///
/// <list type="bullet">
/// <item>The ORIGINAL implementation divided each correction by <c>wA+wB</c> (textbook PBD mass
/// splitting). The real Havok code never does this - every two-particle link scales its correction
/// by <c>invMassA</c>/<c>invMassB</c> DIRECTLY, with no normalisation by their sum at all. Real
/// authored stiffness values are tiny (~0.0003-0.0006 on Zelda's own cape) specifically because
/// they're calibrated against UN-normalised, often large (900-1350) invMass values - dividing by
/// wA+wB on top diluted an already-calibrated correction by another ~2700x, which is why cloth
/// anchored by real data sagged as if barely constrained at all.</item>
/// <item>A later attempt reinterpreted stiffness as XPBD compliance (<c>wTotal + stiffness/dt^2</c>)
/// after noticing tiny Standard Link values look compliance-shaped. That happened to move Standard
/// Link's behaviour roughly right by accident, but does not match the real formula (which has no
/// <c>dt</c> dependence at all) and was actively wrong for Stretch Link (authored as a clean
/// <c>1.0</c> - a "full correction" blend value, not a near-zero compliance) and Bend Stiffness
/// (real values are negative, which a compliance denominator cannot accept safely). This is why
/// that attempt measurably improved the reported shape while making overall stability worse.
/// </item>
/// </list>
/// </summary>
public static class PbdSolver
{
    /// <summary>
    /// Performs Verlet integration predicting particle positions under velocity, gravity, and damping.
    /// </summary>
    /// <param name="gravityScale">
    /// Multiplies the authored gravity vector. Defaults to 1 (exactly as authored). Every real cloth
    /// piece checked authors <c>m_gravity = (0,-9.81,0)</c> - genuine real-world gravity, confirmed
    /// against raw file bytes, not a misread - and <c>m_actions</c> (where a real
    /// <c>hclAirResistanceAction</c>/wind action would live) is EMPTY on every piece, so there is no
    /// authored drag or gravity-scale mechanism this project failed to parse; the file is simply
    /// silent on why TotK's cloth might read as falling more gently than 9.81 m/s^2 in-engine (a
    /// world-level override this format has no room to express, exactly like
    /// <c>hclWorldStepInfo::m_numWorldSubSteps</c> - see <c>ExecuteSimulateOperator</c>'s own
    /// remarks for the same shape of gap). This is therefore an honest, exposed ARTISTIC knob (see
    /// <c>PlacedActor.ClothGravityScale</c>), not a value recovered from data.
    /// </param>
    public static void Integrate(SimClothRuntime runtime, float dt, float gravityScale = 1f)
    {
        if (dt <= 0f) return;

        Vector3 gravity = runtime.Gravity * gravityScale;
        float damping = runtime.GlobalDamping;
        float dampingFactor = MathF.Max(0f, 1f - damping * dt);

        var pos = runtime.Positions;
        var prevPos = runtime.PrevPositions;
        var vel = runtime.Velocities;
        var invMass = runtime.InvMasses;
        var isFixed = runtime.IsFixed;

        for (int i = 0; i < runtime.ParticleCount; i++)
        {
            if (isFixed[i] || invMass[i] <= 0f)
                continue;

            // Velocity from previous displacement
            Vector3 v = (pos[i] - prevPos[i]) / dt;

            // Apply damping and external gravity
            v = v * dampingFactor + gravity * dt;
            vel[i] = v;

            // Advance positions
            prevPos[i] = pos[i];
            pos[i] += v * dt;
        }
    }

    /// <summary>
    /// Solves all constraint sets in a single iteration.
    /// </summary>
    public static void SolveConstraints(
        SimClothRuntime runtime,
        ReadOnlySpan<HclConstraintSet> constraintSets,
        ReadOnlySpan<Vector3> referenceBufferPositions)
    {
        foreach (var cs in constraintSets)
        {
            SolveConstraintSet(runtime, cs, referenceBufferPositions);
        }
    }

    /// <summary>Applies ONE constraint set - the unit the real engine's authored execution order schedules (see <c>HclSimulateOperator.ConstraintExecution</c>).</summary>
    public static void SolveConstraintSet(
        SimClothRuntime runtime,
        HclConstraintSet cs,
        ReadOnlySpan<Vector3> referenceBufferPositions)
    {
        {
            switch (cs)
            {
                case HclStandardLinkConstraintSet standard:
                    SolveStandardLinks(runtime, standard);
                    break;
                case HclStretchLinkConstraintSet stretch:
                    SolveStretchLinks(runtime, stretch);
                    break;
                case HclCompressibleLinkConstraintSet compressible:
                    SolveCompressibleLinks(runtime, compressible);
                    break;
                case HclBendLinkConstraintSet bend:
                    SolveBendLinks(runtime, bend);
                    break;
                case HclBendStiffnessConstraintSet bendStiff:
                    SolveBendStiffness(runtime, bendStiff);
                    break;
                case HclLocalRangeConstraintSet localRange:
                    SolveLocalRanges(runtime, localRange, referenceBufferPositions);
                    break;
            }
        }
    }

    /// <summary>
    /// Real formula, from <c>hclStandardLinkConstraintSet::solveStandardLinkBatch</c>:
    /// <code>
    /// factor = (currentLength - restLength) * stiffness
    /// posA += unitAB * factor * invMassA
    /// posB -= unitAB * factor * invMassB
    /// </code>
    /// where <c>unitAB</c> points from A to B. No division by <c>wA+wB</c> anywhere - see this
    /// class's own remarks for why that matters.
    /// </summary>
    public static void SolveStandardLinks(SimClothRuntime runtime, HclStandardLinkConstraintSet set)
    {
        var pos = runtime.Positions;
        var invMass = runtime.InvMasses;
        var links = set.Links;

        for (int i = 0; i < links.Length; i++)
        {
            ref readonly var link = ref links[i];
            int pA = link.ParticleA;
            int pB = link.ParticleB;
            if ((uint)pA >= (uint)pos.Length || (uint)pB >= (uint)pos.Length) continue;

            // diff = posA - posB, i.e. -unitAB * dist
            Vector3 diff = pos[pA] - pos[pB];
            float dist = diff.Length();
            if (dist < 1e-9f) continue;

            float factor = (dist - link.RestLength) * link.Stiffness;
            Vector3 unitAB = -diff / dist;

            pos[pA] += unitAB * factor * invMass[pA];
            pos[pB] -= unitAB * factor * invMass[pB];
        }
    }

    /// <summary>
    /// Real formula, from <c>hclStretchLinkConstraintSet::solveStretchLinkBatch</c> - a unilateral
    /// "never stretch past restLength" hard clamp. Deliberately asymmetric (only <c>B</c> moves,
    /// per the SDK's own field docs: "m_particleA: index of the fixed particle. m_particleB: index
    /// of the non-fixed particle.") and does NOT weight by mass at all - a genuinely different
    /// mechanism from Standard Link, not just a differently-tuned spring.
    /// <code>
    /// error = min(restLength - currentLength, 0)   // only nonzero (negative) when stretched past rest
    /// posB += unitAB * error * stiffness
    /// </code>
    /// </summary>
    public static void SolveStretchLinks(SimClothRuntime runtime, HclStretchLinkConstraintSet set)
    {
        var pos = runtime.Positions;
        var links = set.Links;

        for (int i = 0; i < links.Length; i++)
        {
            ref readonly var link = ref links[i];
            int pA = link.ParticleA;
            int pB = link.ParticleB;
            if ((uint)pA >= (uint)pos.Length || (uint)pB >= (uint)pos.Length) continue;

            Vector3 diff = pos[pB] - pos[pA];
            float dist = diff.Length();
            if (dist < 1e-9f) continue;

            float error = MathF.Min(link.RestLength - dist, 0f);
            pos[pB] += (diff / dist) * (error * link.Stiffness);
        }
    }

    /// <summary>
    /// Real formula, from <c>hclCompressibleLinkConstraintSet::solveCompressibleLinkBatch</c> -
    /// the mirror image of Standard Link (mass-weighted both ways, no wTotal division), but active
    /// in TWO cases: stretched past <c>restLength</c> (pulls together, target = restLength) OR
    /// compressed past <c>compressionLength</c> (pushes apart, target = compressionLength).
    /// </summary>
    public static void SolveCompressibleLinks(SimClothRuntime runtime, HclCompressibleLinkConstraintSet set)
    {
        var pos = runtime.Positions;
        var invMass = runtime.InvMasses;
        var links = set.Links;

        for (int i = 0; i < links.Length; i++)
        {
            ref readonly var link = ref links[i];
            int pA = link.ParticleA;
            int pB = link.ParticleB;
            if ((uint)pA >= (uint)pos.Length || (uint)pB >= (uint)pos.Length) continue;

            Vector3 diff = pos[pA] - pos[pB];
            float dist = diff.Length();
            if (dist < 1e-9f) continue;

            bool isOnCompression = dist < link.CompressionLength;
            if (dist <= link.RestLength && !isOnCompression) continue;

            float targetLength = isOnCompression ? link.CompressionLength : link.RestLength;
            float factor = (dist - targetLength) * link.Stiffness;
            Vector3 unitAB = -diff / dist;

            pos[pA] += unitAB * factor * invMass[pA];
            pos[pB] -= unitAB * factor * invMass[pB];
        }
    }

    /// <summary>
    /// Real formula, from <c>hclBendLinkConstraintSet::solveBendLinkBatch</c>. Two INDEPENDENT
    /// stiffness values, not one shared value the way this project's model used to conflate them
    /// (see <see cref="BendLink"/>'s own remarks) - bending (compression below
    /// <see cref="BendLink.BendMinLength"/>) and stretching (extension beyond
    /// <see cref="BendLink.StretchMaxLength"/>) are mutually exclusive (the real comment: "This
    /// works because at least one term is zero, since bendMinLength &lt;= stretchMaxLength"), each
    /// with its own stiffness, mass-weighted directly like Standard/Compressible Link (no wTotal
    /// division).
    /// </summary>
    public static void SolveBendLinks(SimClothRuntime runtime, HclBendLinkConstraintSet set)
    {
        var pos = runtime.Positions;
        var invMass = runtime.InvMasses;
        var links = set.Links;

        for (int i = 0; i < links.Length; i++)
        {
            ref readonly var link = ref links[i];
            int pA = link.ParticleA;
            int pB = link.ParticleB;
            if ((uint)pA >= (uint)pos.Length || (uint)pB >= (uint)pos.Length) continue;

            Vector3 diff = pos[pA] - pos[pB];
            float dist = diff.Length();
            if (dist < 1e-9f) continue;

            float bend = MathF.Max(0f, link.BendMinLength - dist);
            float stretch = MathF.Max(0f, dist - link.StretchMaxLength);
            if (bend == 0f && stretch == 0f) continue;

            float factor = stretch * link.StretchStiffness - bend * link.BendStiffness;
            Vector3 unitAB = -diff / dist;

            pos[pA] += unitAB * factor * invMass[pA];
            pos[pB] -= unitAB * factor * invMass[pB];
        }
    }

    /// <summary>
    /// Real formula, from <c>hclBendStiffnessConstraintSet::solveBendStiffness</c> (the
    /// <c>!m_useRestPoseConfig</c> path - Volino's "no rest curvature" bend model): a single
    /// bending vector shared by all four particles, redistributed to each proportional to its own
    /// weight and mass. No division anywhere, so <c>BendStiffness</c>'s real negative authored
    /// values (confirmed on Zelda's cape) are perfectly safe here - it's a plain multiply, not a
    /// denominator.
    /// </summary>
    private static void SolveBendStiffnessSimple(SimClothRuntime runtime, HclBendStiffnessConstraintSet set)
    {
        var pos = runtime.Positions;
        var invMass = runtime.InvMasses;
        var links = set.Links;

        for (int i = 0; i < links.Length; i++)
        {
            ref readonly var link = ref links[i];
            int pA = link.ParticleA;
            int pB = link.ParticleB;
            int pC = link.ParticleC;
            int pD = link.ParticleD;
            if ((uint)pA >= (uint)pos.Length || (uint)pB >= (uint)pos.Length ||
                (uint)pC >= (uint)pos.Length || (uint)pD >= (uint)pos.Length) continue;

            Vector3 r = pos[pA] * link.WeightA + pos[pB] * link.WeightB +
                        pos[pC] * link.WeightC + pos[pD] * link.WeightD;

            pos[pA] += r * (link.BendStiffness * link.WeightA * invMass[pA]);
            pos[pB] += r * (link.BendStiffness * link.WeightB * invMass[pB]);
            pos[pC] += r * (link.BendStiffness * link.WeightC * invMass[pC]);
            pos[pD] += r * (link.BendStiffness * link.WeightD * invMass[pD]);
        }
    }

    /// <summary>
    /// Real formula, from <c>hclBendStiffnessConstraintSet::solveBendStiffnessRestPoseConfig</c>
    /// (the <c>m_useRestPoseConfig</c> path - drives toward the ORIGINAL authored rest curvature
    /// instead of a flat configuration). Recomputes the true edge-normal-based curvature every
    /// call rather than the simple path's plain weighted sum. Deliberately does NOT implement the
    /// SDK's optional <c>ClampBendStiffness</c> energy limiter (<c>NoClampBendStiffnessFunctor</c>'s
    /// own body is empty in the real source - it's genuinely a no-op - so this only skips the
    /// `true` branch's extra safety clamp, not a behaviour any cloth relies on for its baseline look).
    /// </summary>
    private static void SolveBendStiffnessRestPoseConfig(SimClothRuntime runtime, HclBendStiffnessConstraintSet set)
    {
        var pos = runtime.Positions;
        var invMass = runtime.InvMasses;
        var links = set.Links;

        for (int i = 0; i < links.Length; i++)
        {
            ref readonly var link = ref links[i];
            int pA = link.ParticleA;
            int pB = link.ParticleB;
            int pC = link.ParticleC;
            int pD = link.ParticleD;
            if ((uint)pA >= (uint)pos.Length || (uint)pB >= (uint)pos.Length ||
                (uint)pC >= (uint)pos.Length || (uint)pD >= (uint)pos.Length) continue;

            Vector3 c = pos[pC];
            Vector3 edgeCA = pos[pA] - c;
            Vector3 edgeCB = pos[pB] - c;
            Vector3 edgeCD = pos[pD] - c;

            Vector3 normalC = Vector3.Cross(edgeCD, edgeCA);
            Vector3 normalD = Vector3.Cross(edgeCB, edgeCD);
            float lengthNC = normalC.Length();
            float lengthND = normalD.Length();
            if (lengthNC > 1e-9f) normalC /= lengthNC; else normalC = Vector3.Zero;
            if (lengthND > 1e-9f) normalD /= lengthND; else normalD = Vector3.Zero;

            Vector3 restOffset = normalC + normalD;
            float restOffsetLen = restOffset.Length();
            restOffset = restOffsetLen > 1e-9f ? restOffset / restOffsetLen : Vector3.Zero;

            float edgeCDLenSq = edgeCD.LengthSquared();
            float invBaseSq = edgeCDLenSq > 1e-9f ? 1f / edgeCDLenSq : 0f;

            float initialRestCurvature = link.RestCurvature;
            float currentRestCurvature = lengthNC * lengthND * invBaseSq;

            restOffset *= currentRestCurvature;

            Vector3 r = pos[pA] * link.WeightA + pos[pB] * link.WeightB +
                        pos[pC] * link.WeightC + pos[pD] * link.WeightD +
                        restOffset * initialRestCurvature;

            pos[pA] += r * (link.BendStiffness * link.WeightA * invMass[pA]);
            pos[pB] += r * (link.BendStiffness * link.WeightB * invMass[pB]);
            pos[pC] += r * (link.BendStiffness * link.WeightC * invMass[pC]);
            pos[pD] += r * (link.BendStiffness * link.WeightD * invMass[pD]);
        }
    }

    public static void SolveBendStiffness(SimClothRuntime runtime, HclBendStiffnessConstraintSet set)
    {
        if (set.UseRestPoseConfig)
            SolveBendStiffnessRestPoseConfig(runtime, set);
        else
            SolveBendStiffnessSimple(runtime, set);
    }

    /// <summary>
    /// Matches the real <c>hclLocalRangeConstraintSet::_executeLocalRangeP</c> (sphere shape, no
    /// normal component) exactly - verified algebraically equal to the real
    /// <c>pos += unitDiff * min(maxDist - dist, 0) * stiffness</c> form, just expressed as a Lerp
    /// toward the clamped target point; unlike every constraint above, this one was never wrong.
    /// </summary>
    public static void SolveLocalRanges(
        SimClothRuntime runtime,
        HclLocalRangeConstraintSet set,
        ReadOnlySpan<Vector3> referenceBufferPositions)
    {
        if (referenceBufferPositions.IsEmpty) return;

        var pos = runtime.Positions;
        var invMass = runtime.InvMasses;
        var constraints = set.LocalConstraints;

        // Real: lrStiffness = m_stiffness * constraintStiffness (the dispatcher's global, 1.0 here),
        // then localStiffness = localConstraint.getStiffness() * lrStiffness.
        float setStiffness = set.Stiffness > 0f ? set.Stiffness : 1f;

        for (int i = 0; i < constraints.Length; i++)
        {
            ref readonly var c = ref constraints[i];
            int pIdx = c.ParticleIndex;
            int refIdx = c.ReferenceVertex;

            if (pIdx >= runtime.ParticleCount || refIdx >= referenceBufferPositions.Length)
                continue;

            if (invMass[pIdx] <= 0f)
                continue; // Fixed particle already locked

            float stiffness = Math.Clamp(c.Stiffness * setStiffness, 0f, 1f);
            if (stiffness <= 0f)
                continue;

            Vector3 refPos = referenceBufferPositions[refIdx];
            Vector3 diff = pos[pIdx] - refPos;
            float dist = diff.Length();

            if (c.MaxDistance > 0f && dist > c.MaxDistance && dist > 1e-9f)
            {
                // Pull back within MaxDistance
                Vector3 target = refPos + diff * (c.MaxDistance / dist);
                pos[pIdx] = Vector3.Lerp(pos[pIdx], target, stiffness);
            }
        }
    }
}
