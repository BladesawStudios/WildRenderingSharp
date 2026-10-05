using System;
using System.Collections.Generic;
using System.Numerics;
using WildRenderingSharp.Cloth.Model;
using WildRenderingSharp.Cloth.Model.Animation;
using WildRenderingSharp.Cloth.Model.Collidables;
using WildRenderingSharp.Cloth.Model.Operators;
using WildRenderingSharp.Cloth.Simulation.Solvers;

namespace WildRenderingSharp.Cloth.Simulation;

/// <summary>
/// A simulated runtime instance for a single cloth piece (HclClothData).
/// Coordinates runtime particle buffers, operator pipelines, constraint solving, and bone deform output.
/// </summary>
public sealed class ClothInstance
{
    public HclClothData ClothData { get; }
    public HkaSkeleton? Skeleton { get; }

    public List<SimClothRuntime> Runtimes { get; } = new();
    public Vector3[][] Buffers { get; }
    public Matrix4x4[] SkeletonTransforms { get; }
    public HashSet<int> DeformedBoneIndices { get; } = new();

    public bool IsEnabled { get; set; } = true;
    public Vector3 Wind { get; set; } = Vector3.Zero;

    /// <summary>Multiplies authored gravity - see <c>PbdSolver.Integrate</c>'s remarks on why this is an artistic knob, not a data-derived one.</summary>
    public float GravityScale { get; set; } = 1f;

    public ClothInstance(HclClothData clothData, HkaSkeleton? skeleton = null)
    {
        ClothData = clothData ?? throw new ArgumentNullException(nameof(clothData));
        Skeleton = skeleton;

        // Track deformed bone indices from any mesh bone deform operators
        foreach (var op in clothData.Operators)
        {
            if (op is HclSimpleMeshBoneDeformOperator meshBoneOp)
            {
                foreach (var pair in meshBoneOp.TriangleBonePairs)
                {
                    DeformedBoneIndices.Add(pair.BoneOffset / 64);
                }
            }
        }

        // Initialize SimCloth runtimes
        foreach (var simData in clothData.SimClothDatas)
        {
            Runtimes.Add(new SimClothRuntime(simData));
        }

        // Allocate vertex buffers based on BufferDefinitions
        int bufferCount = Math.Max(1, clothData.BufferDefinitions.Count);
        Buffers = new Vector3[bufferCount][];
        for (int i = 0; i < clothData.BufferDefinitions.Count; i++)
        {
            var def = clothData.BufferDefinitions[i];
            int vertCount = (int)def.NumVertices;
            if (vertCount == 0 && Runtimes.Count > 0)
                vertCount = Runtimes[0].ParticleCount;

            Buffers[i] = new Vector3[vertCount];
        }

        // Allocate skeleton transforms matching both the operator transform set and the embedded
        // skeleton. Some skin/deform operators address bones beyond NumTransforms even though the
        // collidable map itself stays within that authored transform set.
        int numTransforms = 0;
        if (clothData.TransformSetDefinitions.Count > 0)
        {
            numTransforms = (int)clothData.TransformSetDefinitions[0].NumTransforms;
        }
        if (skeleton != null)
        {
            numTransforms = Math.Max(numTransforms, skeleton.Bones.Count);
        }

        SkeletonTransforms = new Matrix4x4[numTransforms];
        Reset();
    }

    /// <summary>
    /// Resets all simulated particles and skeleton transforms to rest poses.
    /// </summary>
    public void Reset()
    {
        _primed = false;

        for (int i = 0; i < Runtimes.Count; i++)
        {
            Runtimes[i].ResetToPose(0);
        }

        // Initialize skeleton transforms with reference pose if available
        if (Skeleton != null)
        {
            var worldRefPose = Skeleton.WorldReferencePose;
            for (int i = 0; i < SkeletonTransforms.Length; i++)
            {
                SkeletonTransforms[i] = i < worldRefPose.Count ? worldRefPose[i] : Matrix4x4.Identity;
            }
        }
        else
        {
            Array.Fill(SkeletonTransforms, Matrix4x4.Identity);
        }

        // Initialize buffers with pose 0
        if (Runtimes.Count > 0 && Buffers.Length > 0)
        {
            var runtime0 = Runtimes[0];
            for (int b = 0; b < Buffers.Length; b++)
            {
                int copyLen = Math.Min(Buffers[b].Length, runtime0.Positions.Length);
                Array.Copy(runtime0.Positions, Buffers[b], copyLen);
            }
        }
    }

    /// <summary>
    /// Advances the cloth simulation by dt seconds given the current animated input skeleton.
    /// </summary>
    public void Step(float dt, ReadOnlySpan<Matrix4x4> inputSkeletonTransforms) =>
        Step(dt, inputSkeletonTransforms, Matrix4x4.Identity);

    /// <summary>
    /// Advances the cloth simulation by dt seconds given the current animated input skeleton.
    /// </summary>
    /// <param name="referenceFrameDelta">
    /// Maps a point that was correctly placed in last Step's OBJECT space into where it must sit in
    /// THIS frame's object space to represent the same absolute world point - i.e.
    /// <c>worldFromObject(prevFrame) * objectFromWorld(thisFrame)</c>. Positions here are simulated
    /// in object space (see the class remarks), which by itself makes cloth perfectly rigid under
    /// whole-object motion - moving/rotating the object doesn't touch any bone matrix, so nothing
    /// would visibly react. Identity (the default, and what a stationary actor naturally computes,
    /// or a discontinuous jump - see <c>PlacedActor</c>'s own remarks) is a no-op.
    ///
    /// The real mechanism this reproduces is Havok Cloth's own "Transfer Motion"
    /// (<c>hclCharacterUtils::transferMotion</c>/<c>_transformParticleForSimulateOperator</c>,
    /// confirmed against the real SDK source): free particles' CURRENT AND PREVIOUS positions both
    /// get rigidly carried along by a BLENDED fraction of this frame's actor motion - not the raw
    /// delta applied to just one of the two (an earlier version of this feature did exactly that,
    /// to previous-only, which reads as a full, instantaneous velocity kick equal to the ENTIRE
    /// frame's motion divided by dt - unboundedly large for any real movement speed, and exactly
    /// why it "reacted so strongly"). Moving both current and previous by the SAME blended rigid
    /// transform injects NO extra velocity for the transferred (blended) portion at all - a rigid
    /// transform's effect on a position DIFFERENCE only rotates it, translation cancels out - so
    /// the only thing that produces lag is whatever fraction of the motion DOESN'T get blended in;
    /// gravity and the constraint graph settle that remainder naturally over the following frames
    /// instead of it showing up as a sudden snap. The blend itself favours "mostly rigid" at rest
    /// or slow motion and eases toward "more lag" as the actor moves faster - real Havok authors
    /// this per-cloth via <c>hclSimClothData::TransferMotionData</c>'s own min/max speed and blend
    /// fields, which this project doesn't parse, so the specific curve here
    /// (<see cref="BlendFromSpeed"/>) is a reasonable approximation, not the authored one.
    /// Fixed particles are excluded - they're re-driven from bone matrices every step regardless.
    /// </param>
    /// <summary>
    /// Whether this instance has run its first real simulated step yet - see the remarks on
    /// <see cref="Step(float, ReadOnlySpan{Matrix4x4}, Matrix4x4)"/> for why that first step is
    /// special-cased.
    /// </summary>
    private bool _primed;

    public void Step(float dt, ReadOnlySpan<Matrix4x4> inputSkeletonTransforms, Matrix4x4 referenceFrameDelta)
    {
        if (!IsEnabled || dt <= 0f) return;

        if (!_primed)
        {
            // Reset() seeds every particle from the file's own static bind pose (frequently a
            // T-/A-pose, wildly different from wherever the actor's real first animated frame or
            // idle stance actually has its bones), and the skin operator's anchors snap straight to
            // THIS frame's real pose on the very first Step call regardless. The result is a
            // whole-chain, one-frame discontinuity between freshly-repositioned anchors and
            // still-bind-pose free particles that reads as an explosive "freak out" burst right as
            // the actor spawns, before gravity/constraints pull it back to a normal hang over the
            // next several frames - not something the real game ever shows, because a real engine
            // settles cloth onto its actual spawn pose before the model is ever drawn. Reproduce
            // that here: run several extra solve passes against THIS frame's real anchors before
            // this instance is ever visible, then erase whatever velocity that settling left behind
            // so the first frame anything actually renders starts from a clean, at-rest state
            // instead of carrying a burst of "spawn shove" into frame 2.
            //
            // Two earlier attempts at this settle burst each fixed one accessory at the cost of
            // another, because both fed every settle iteration the SAME already-fully-snapped
            // target pose and only argued about whether collision should run against it:
            //   - Collision ON throughout: a piece bind-pose-adjacent to a body capsule (a gown
            //     flap) can have its free particles land on either side of that capsule the instant
            //     the anchor snaps to the real pose, and collision then HOLDS whichever side it
            //     first touched for the rest of the burst - a flap stuck permanently against the
            //     wrong face of a leg capsule, never able to cross back.
            //   - Collision OFF throughout: long hair chains that are SUPPOSED to be held off the
            //     head/neck by that same collision are instead free to fall straight through it for
            //     12 full sub-steps of unopposed gravity, arriving deeply interpenetrated - so the
            //     very first real (collision-enabled) frame has to shove the whole chain back out
            //     in one step, which is the same violent "freak out" this burst exists to prevent.
            //
            // The real fix is to never present a discontinuous pose in the first place: interpolate
            // the SKELETON itself from the bind pose Reset() left in SkeletonTransforms up to the
            // real target pose across the settle iterations (translation lerp, rotation slerp per
            // bone), with collision left ON the whole time. Anchors then sweep smoothly toward their
            // target instead of teleporting past a capsule - collision gets a real chance to push
            // back at every intermediate step (so hair can't tunnel through the head/neck) and a
            // flap can't "already be on the far side" before collision ever sees it (so it can't get
            // locked to the wrong face either).
            var bindPose = (Matrix4x4[])SkeletonTransforms.Clone();
            var blended = new Matrix4x4[SkeletonTransforms.Length];
            const int SettleSteps = 12;
            for (int i = 0; i < SettleSteps; i++)
            {
                float t = (i + 1) / (float)SettleSteps;
                BlendSkeleton(bindPose, inputSkeletonTransforms, t, blended);
                RunOnce(dt, blended, referenceFrameDelta, enableCollision: true);
            }

            foreach (var runtime in Runtimes)
                Array.Copy(runtime.Positions, runtime.PrevPositions, runtime.Positions.Length);

            _primed = true;
            return;
        }

        RunOnce(dt, inputSkeletonTransforms, referenceFrameDelta, enableCollision: true);
    }

    /// <summary>Per-bone translation-lerp/rotation-slerp blend between two skeleton poses, used only to sweep the priming burst's target pose in smoothly - see <see cref="Step(float, ReadOnlySpan{Matrix4x4}, Matrix4x4)"/>'s remarks.</summary>
    private static void BlendSkeleton(ReadOnlySpan<Matrix4x4> from, ReadOnlySpan<Matrix4x4> to, float t, Matrix4x4[] result)
    {
        int count = Math.Min(from.Length, Math.Min(to.Length, result.Length));
        for (int i = 0; i < count; i++)
        {
            if (Matrix4x4.Decompose(from[i], out Vector3 sFrom, out Quaternion rFrom, out Vector3 tFrom) &&
                Matrix4x4.Decompose(to[i], out Vector3 sTo, out Quaternion rTo, out Vector3 tTo))
            {
                Vector3 scale = Vector3.Lerp(sFrom, sTo, t);
                Quaternion rot = Quaternion.Slerp(rFrom, rTo, t);
                Vector3 trans = Vector3.Lerp(tFrom, tTo, t);
                result[i] = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(trans);
            }
            else
            {
                result[i] = to[i]; // non-decomposable (degenerate) matrix - jump straight to target rather than guess
            }
        }
        for (int i = count; i < result.Length; i++)
            result[i] = i < to.Length ? to[i] : Matrix4x4.Identity;
    }

    private void RunOnce(float dt, ReadOnlySpan<Matrix4x4> inputSkeletonTransforms, Matrix4x4 referenceFrameDelta, bool enableCollision)
    {
        // Copy input skeleton transforms into local transform set
        int copyLen = Math.Min(SkeletonTransforms.Length, inputSkeletonTransforms.Length);
        inputSkeletonTransforms[..copyLen].CopyTo(SkeletonTransforms.AsSpan(0, copyLen));

        if (!referenceFrameDelta.IsIdentity && Matrix4x4.Decompose(referenceFrameDelta, out _, out Quaternion deltaRot, out Vector3 deltaTrans))
        {
            float translateSpeed = deltaTrans.Length() / dt;
            float rotAngle = 2f * MathF.Acos(Math.Clamp(MathF.Abs(deltaRot.W), 0f, 1f));
            float rotateSpeed = rotAngle / dt;

            // "Mostly rigid" (0.85) below a slow walk, easing down to "noticeably lagging" (0.35)
            // past a run - the actual shape of the curve matters less than that it's neither 0
            // (cloth never tracks the body at all) nor 1 (no lag ever, the original bug) at any
            // real speed.
            float translateBlend = BlendFromSpeed(translateSpeed, minSpeed: 0.05f, maxSpeed: 3f, minBlend: 0.85f, maxBlend: 0.35f);
            float rotateBlend = BlendFromSpeed(rotateSpeed, minSpeed: 0.2f, maxSpeed: 6f, minBlend: 0.85f, maxBlend: 0.35f);

            Vector3 blendedTrans = deltaTrans * translateBlend;
            Quaternion blendedRot = Quaternion.Identity;
            if (rotAngle > 1e-6f)
            {
                Vector3 axis = new Vector3(deltaRot.X, deltaRot.Y, deltaRot.Z) / MathF.Sin(rotAngle * 0.5f);
                blendedRot = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), rotAngle * rotateBlend);
            }
            Matrix4x4 blendedDelta = Matrix4x4.CreateFromQuaternion(blendedRot) * Matrix4x4.CreateTranslation(blendedTrans);

            foreach (var runtime in Runtimes)
            {
                var pos = runtime.Positions;
                var prevPos = runtime.PrevPositions;
                var isFixed = runtime.IsFixed;
                for (int i = 0; i < pos.Length; i++)
                {
                    if (isFixed[i]) continue;
                    pos[i] = Vector3.Transform(pos[i], blendedDelta);
                    prevPos[i] = Vector3.Transform(prevPos[i], blendedDelta);
                }
            }
        }

        // Execute operators in sequence
        foreach (var op in ClothData.Operators)
        {
            switch (op)
            {
                case HclObjectSpaceSkinOperator skinOp:
                    ExecuteSkinOperator(skinOp, SkeletonTransforms);
                    break;
                case HclMoveParticlesOperator moveOp:
                    ExecuteMoveParticlesOperator(moveOp);
                    break;
                case HclSimulateOperator simOp:
                    ExecuteSimulateOperator(simOp, dt, SkeletonTransforms, enableCollision);
                    break;
                case HclSimpleMeshBoneDeformOperator meshBoneOp:
                    ExecuteMeshBoneOperator(meshBoneOp);
                    break;
            }
        }
    }

    /// <summary>Linear ramp from <paramref name="minBlend"/> at/below <paramref name="minSpeed"/> to <paramref name="maxBlend"/> at/above <paramref name="maxSpeed"/> - see <see cref="Step(float, ReadOnlySpan{Matrix4x4}, Matrix4x4)"/>'s own remarks for what this blend means physically.</summary>
    private static float BlendFromSpeed(float speed, float minSpeed, float maxSpeed, float minBlend, float maxBlend)
    {
        if (speed <= minSpeed) return minBlend;
        if (speed >= maxSpeed) return maxBlend;
        float t = (speed - minSpeed) / (maxSpeed - minSpeed);
        return minBlend + (maxBlend - minBlend) * t;
    }

    /// <summary>Per-operator calibration and per-vertex bone assignment, keyed by OperatorId and lazily built once by <see cref="PrepareSkinOperator"/> - see its remarks for why both exist.</summary>
    private readonly Dictionary<uint, (Matrix4x4[] InverseBind, int[] BoneAssignment)> _skinOperatorSetup = new();

    /// <summary>
    /// Real hclObjectSpaceSkinOperator drives this from a per-vertex, up-to-8-bone compressed
    /// blend-weight table (Havok's own hclObjectSpaceDeformer, confirmed via Ghidra against the
    /// game binary's own hkClass reflection data - it declares One..EightBlendEntryBlock variants
    /// plus P/PN/PNT/PNTB "local block" packings). That per-vertex weight table isn't parsed by
    /// this project yet, so this applies a SINGLE dominant bone per vertex (by nearest bind-pose
    /// origin - see <see cref="PrepareSkinOperator"/>) instead of a true multi-bone blend. Still a
    /// large improvement over the previous behaviour, which just copied the bind pose straight
    /// through with the animated skeleton having no effect on it at all - the direct cause of cloth
    /// anchors staying frozen at bind pose while the body moved (feeding the simulation a fixed
    /// reference under stretched-to-breaking constraints every frame, i.e. exactly the "blown
    /// weirdly, bones everywhere" symptom).
    /// </summary>
    private void ExecuteSkinOperator(HclObjectSpaceSkinOperator op, ReadOnlySpan<Matrix4x4> transforms)
    {
        int outBufIdx = (int)op.OutputBufferIndex;
        if (outBufIdx >= Buffers.Length) return;

        var outBuf = Buffers[outBufIdx];
        if (Runtimes.Count == 0) return;

        if (op.Deformer.IsUsable && ExecuteAuthoredObjectSpaceDeformer(op, transforms, outBuf))
            return;

        // SimulationMesh_Pod_C and SimulationMesh_Bag declare their skin output as buffer 1 while
        // their MoveFixedParticles operator reads buffer 0, which nothing else ever writes - so
        // their anchors sat at the Reset() rest pose forever and those pieces, like Rope_1 above,
        // were completely inert. In the real engine the two buffers are reconciled by the
        // VertexGather operators this project only partially models (see
        // HclGatherAllVerticesOperator). Until gather is fully honoured, publish the kinematic
        // skinned reference into the buffer the piece's own MoveParticles names as well, since that
        // is by definition where the anchor source is meant to live.
        int aliasBufIdx = -1;
        foreach (var other in ClothData.Operators)
        {
            if (other is HclMoveParticlesOperator mp && mp.RefBufferIndex != outBufIdx && mp.RefBufferIndex < Buffers.Length)
            {
                aliasBufIdx = (int)mp.RefBufferIndex;
                break;
            }
        }
        var restPositions = Runtimes[0].Data.Poses.Length > 0 ? Runtimes[0].Data.Poses[0].Positions : Runtimes[0].Positions;

        int particleCount = restPositions.Length;

        // SimulationMesh_Rope_1 authors a skin operator with real BoneFromSkinMeshTransforms but an
        // EMPTY m_transformSubset. Taking boneCount = min(subset, bfsm) then yielded 0 and dropped
        // the whole piece down the "no skeleton" path below, which copies rest positions through
        // unchanged - so that piece was never skinned at all, its reference buffer never followed
        // the body, its anchors stayed frozen at bind pose and it could neither swing nor react to
        // gravity however the simulation behaved. An empty subset means "no restriction", so every
        // bone of the piece's own transform set is a candidate; the nearest-bone search below (which
        // already excludes this cloth's own simulated output bones) then picks among them exactly as
        // it does for every other piece.
        // Validate the parsed subset before trusting it. SimulationMesh_Pod_C and
        // SimulationMesh_Bag parse a TransformSubset of hundreds of entries containing values like
        // 16, 88, 600 and fragments of ASCII - i.e. raw file bytes - against a transform set of only
        // 7 bones, because those two pieces evidently use a skin-operator VARIANT whose field layout
        // differs from hclObjectSpaceSkinOperator's (the SDK also ships PN/PNT/PNTB variants with
        // extra buffers ahead of these fields), so the array header we read points at unrelated
        // data. Trusting it fed every vertex a bone index past the end of the transform set, which
        // silently degraded to Identity for both the bind inverse and the bone matrix - the piece
        // was written out at its REST position every frame, never following the body and never
        // reacting, which is exactly how a hip accessory ends up "in the correct spot and the
        // correct scale but completely stiff". An index outside the piece's own transform set cannot
        // be meaningful, so fall back to the same all-bones candidate set used for an empty subset.
        int numTransforms = Skeleton != null ? Math.Min(SkeletonTransforms.Length, Skeleton.WorldReferencePose.Count) : 0;
        bool subsetUsable = op.TransformSubset.Length > 0 && op.TransformSubset.Length <= numTransforms;
        if (subsetUsable)
        {
            foreach (ushort gi in op.TransformSubset)
            {
                if (gi >= numTransforms) { subsetUsable = false; break; }
            }
        }

        var subset = subsetUsable ? op.TransformSubset : Array.Empty<ushort>();
        int boneCount;
        if (subset.Length == 0)
        {
            int n = numTransforms;
            subset = new ushort[n];
            for (int i = 0; i < n; i++) subset[i] = (ushort)i;
            boneCount = n;
        }
        else
        {
            boneCount = Math.Min(subset.Length, op.BoneFromSkinMeshTransforms.Length);
        }

        var vertexToParticle = GetReferenceVertexToParticleMap(outBuf.Length);

        if (Skeleton == null || boneCount == 0)
        {
            if (vertexToParticle == null)
            {
                Array.Copy(restPositions, outBuf, Math.Min(outBuf.Length, particleCount));
            }
            else
            {
                for (int v = 0; v < outBuf.Length; v++)
                {
                    int p = vertexToParticle[v];
                    if (p >= 0 && p < particleCount) outBuf[v] = restPositions[p];
                }
            }
            return;
        }

        if (!_skinOperatorSetup.TryGetValue(op.OperatorId, out var setup) || setup.BoneAssignment.Length != particleCount)
        {
            setup = PrepareSkinOperator(op, subset, restPositions, particleCount, boneCount);
            _skinOperatorSetup[op.OperatorId] = setup;
        }

        for (int v = 0; v < outBuf.Length; v++)
        {
            // Which PARTICLE's rest position this reference-buffer vertex carries. The two are NOT
            // the same index space (see GetReferenceVertexToParticleMap) - assuming they were is
            // what used to leave most of a hair piece's reference buffer sitting at the origin.
            int p = vertexToParticle != null ? vertexToParticle[v] : (v < particleCount ? v : -1);
            if (p < 0 || p >= particleCount) continue;

            int localBone = setup.BoneAssignment[p];
            int globalIdx = subset[localBone];
            Matrix4x4 currentBoneWorld = globalIdx < transforms.Length ? transforms[globalIdx] : Matrix4x4.Identity;
            Matrix4x4 skinMatrix = setup.InverseBind[localBone] * currentBoneWorld;

            Vector3 skinned = Vector3.Transform(restPositions[p], skinMatrix);
            outBuf[v] = skinned;
            if (aliasBufIdx >= 0 && v < Buffers[aliasBufIdx].Length) Buffers[aliasBufIdx][v] = skinned;
        }
    }

    /// <summary>
    /// Faithful position-only execution of Havok's <c>hclObjectSpaceSkinPOperator</c>. This follows
    /// the SDK's control stream exactly: select the next 16-vertex block for that blend width,
    /// unpack its matching local-position block, linearly blend the authored composite matrices,
    /// and transform the position. Unlike the legacy fallback below, no weights or bone assignment
    /// are inferred from geometry.
    /// </summary>
    private bool ExecuteAuthoredObjectSpaceDeformer(
        HclObjectSpaceSkinOperator op,
        ReadOnlySpan<Matrix4x4> transforms,
        Vector3[] output)
    {
        var d = op.Deformer;
        bool useSubset = op.TransformSubset.Length > 0;
        int compositeCount = useSubset
            ? Math.Min(op.TransformSubset.Length, op.BoneFromSkinMeshTransforms.Length)
            : Math.Min(transforms.Length, op.BoneFromSkinMeshTransforms.Length);
        if (compositeCount <= 0) return false;

        var composites = new Matrix4x4[compositeCount];
        for (int i = 0; i < compositeCount; i++)
        {
            int transformIndex = useSubset ? op.TransformSubset[i] : i;
            if ((uint)transformIndex >= (uint)transforms.Length) return false;

            // SDK column-vector order: meshFromWorld * currentBone * boneFromSkinMesh.
            // System.Numerics uses row vectors, so the equivalent order is reversed. Cloth buffers
            // in WildRenderingSharp are already object/skin-mesh space, hence meshFromWorld is Identity here.
            composites[i] = op.BoneFromSkinMeshTransforms[i] * transforms[transformIndex];
        }

        int[] nextBlock = new int[8];
        int localBlock = 0;
        Vector3[][] locals = d.PackedLocalPositions.Length > 0
            ? d.PackedLocalPositions
            : d.UnpackedLocalPositions;

        foreach (byte control in d.ControlBytes)
        {
            if (control == 4) continue; // NEXT_SPU_BATCH is deliberately a no-op on the SDK CPU path.
            int influences = control switch
            {
                0 => 4,
                1 => 3,
                2 => 2,
                3 => 1,
                5 => 8,
                6 => 7,
                7 => 6,
                8 => 5,
                _ => 0
            };
            if (influences == 0 || localBlock >= locals.Length) return false;

            int blockIndex = nextBlock[influences - 1]++;
            var available = d.BlendBlocks[influences - 1];
            if ((uint)blockIndex >= (uint)available.Length) return false;
            var block = available[blockIndex];
            Vector3[] localPositions = locals[localBlock++];

            for (int vertex = 0; vertex < 16; vertex++)
            {
                int outputIndex = block.VertexIndices[vertex];
                if ((uint)outputIndex >= (uint)output.Length) continue;

                Matrix4x4 blended = default;
                int influenceBase = vertex * influences;
                for (int influence = 0; influence < influences; influence++)
                {
                    int flatIndex = influenceBase + influence;
                    int bone = block.BoneIndices[flatIndex];
                    if ((uint)bone >= (uint)composites.Length) return false;
                    AddWeighted(ref blended, composites[bone], block.BoneWeights[flatIndex]);
                }
                output[outputIndex] = Vector3.Transform(localPositions[vertex], blended);
            }
        }

        // Some exported pieces route MoveParticles through a second reference-buffer index. Until
        // GatherAllVertices is executed in the operator loop, mirror precisely the vertices this
        // skin operator wrote, preserving the established buffer-alias behaviour.
        foreach (var other in ClothData.Operators)
        {
            if (other is not HclMoveParticlesOperator move || move.RefBufferIndex == op.OutputBufferIndex ||
                move.RefBufferIndex >= Buffers.Length) continue;
            Vector3[] alias = Buffers[move.RefBufferIndex];
            int copyCount = Math.Min(alias.Length, output.Length);
            Array.Copy(output, alias, copyCount);
            break;
        }
        return true;
    }

    private static void AddWeighted(ref Matrix4x4 target, in Matrix4x4 value, float weight)
    {
        target.M11 += value.M11 * weight; target.M12 += value.M12 * weight; target.M13 += value.M13 * weight; target.M14 += value.M14 * weight;
        target.M21 += value.M21 * weight; target.M22 += value.M22 * weight; target.M23 += value.M23 * weight; target.M24 += value.M24 * weight;
        target.M31 += value.M31 * weight; target.M32 += value.M32 * weight; target.M33 += value.M33 * weight; target.M34 += value.M34 * weight;
        target.M41 += value.M41 * weight; target.M42 += value.M42 * weight; target.M43 += value.M43 * weight; target.M44 += value.M44 * weight;
    }

    private int[]? _referenceVertexToParticle;
    private bool _referenceMapBuilt;

    /// <summary>
    /// Maps a REFERENCE BUFFER vertex index onto the simulated particle it corresponds to, or -1.
    ///
    /// <para>The reference buffer is a mesh in its own right and its vertex numbering is unrelated to
    /// particle numbering - on <c>SimulationMesh_Hair_B_2</c> the file states <c>vertex 21 -> particle
    /// 0</c> and <c>particle 21 &lt;- vertex 39</c>, in a 40-vertex buffer over 28 particles. This
    /// class used to skin the buffer as though <c>buffer[i]</c> were <c>particle[i]</c> and to write
    /// only <c>min(bufferLength, particleCount)</c> entries, which for every piece whose buffer is
    /// LARGER than its particle count (Zelda's three back-hair pieces, Mant_F and Rope_1 - precisely
    /// the pieces that misbehaved) left the whole upper region of the buffer at its
    /// <see cref="Reset"/> value of <c>Vector3.Zero</c>, i.e. the world origin. Both consumers index
    /// exactly that region: <c>HclMoveParticlesOperator</c> reads it to place the cloth's ANCHORS and
    /// <c>HclLocalRangeConstraintSet</c> reads it as the point each particle is tethered to. So the
    /// hair's anchors were being pinned to (0,0,0) and its tethers were dragging it toward the origin
    /// every frame - metres away from Zelda's head.</para>
    ///
    /// <para>Havok's real answer is <c>hclObjectSpaceDeformer</c>, which carries a genuine per-vertex
    /// local position plus 1-8 weighted bones for every vertex of the buffer; this project has never
    /// parsed it (see <see cref="PrepareSkinOperator"/>). What IS available is the explicit
    /// correspondence the file states twice over - every <c>VertexParticlePair</c> and every local
    /// range constraint names a (vertex, particle) pair - and on the real data those two lists
    /// together cover every particle exactly once, so every vertex that is actually READ has a known
    /// particle. Vertices named by neither are read by nothing and are left alone. Returns null when
    /// the file states no correspondences at all, in which case the caller keeps the old positional
    /// assumption rather than blanking the buffer.</para>
    /// </summary>
    private int[]? GetReferenceVertexToParticleMap(int bufferLength)
    {
        if (_referenceMapBuilt && _referenceVertexToParticle?.Length >= bufferLength)
            return _referenceVertexToParticle;

        var map = new int[bufferLength];
        Array.Fill(map, -1);
        bool any = false;

        foreach (var op in ClothData.Operators)
        {
            if (op is not HclMoveParticlesOperator moveOp) continue;
            foreach (var pair in moveOp.VertexParticlePairs)
            {
                if (pair.VertexIndex < bufferLength)
                {
                    map[pair.VertexIndex] = pair.ParticleIndex;
                    any = true;
                }
            }
        }

        foreach (var sim in ClothData.SimClothDatas)
        {
            foreach (var cs in sim.ConstraintSets)
            {
                if (cs is not Model.Constraints.HclLocalRangeConstraintSet lr) continue;
                foreach (var c in lr.LocalConstraints)
                {
                    if (c.ReferenceVertex < bufferLength)
                    {
                        map[c.ReferenceVertex] = c.ParticleIndex;
                        any = true;
                    }
                }
            }
        }

        _referenceVertexToParticle = any ? map : null;
        _referenceMapBuilt = true;
        return _referenceVertexToParticle;
    }

    /// <summary>
    /// One-time setup for a skin operator: a shared calibration matrix plus a per-vertex bone pick.
    ///
    /// <para><b>Calibration.</b> <c>BoneFromSkinMeshTransforms[b]</c> is meant to be the inverse of
    /// bone b's true bind-pose world matrix, so round-tripping a rest vertex through
    /// <c>bfsm[b] * trueBindWorld[b]</c> should reproduce it exactly. Measured against a real
    /// exported cloth (Armor_001_RaulSkin_Upper's belt), <c>bfsm[b] * WorldReferencePose[b]</c>
    /// (this project's own reconstruction of that bind world, via <see cref="HkaSkeleton.WorldReferencePose"/>)
    /// comes out to the SAME constant matrix for every b in the subset, to float precision -
    /// not close to identity, but a fixed non-identity residual. That means
    /// <c>trueBindWorld[b] = WorldReferencePose[b] * Calibration</c> for a single shared Calibration
    /// (solved from any one bone, since it provably doesn't vary), rather than
    /// WorldReferencePose already being what bfsm expects - some coordinate convention this project
    /// doesn't reproduce (candidates: a pivot/root transform outside the cloth's own small
    /// hkaSkeleton, an axis/units convention difference) shifts every bone's world matrix by the
    /// same fixed amount. Solving Calibration once and folding it into every skin matrix
    /// (<c>bfsm[b] * currentBoneWorld[b] * Calibration</c>) makes the whole operator reproduce the
    /// rest pose exactly regardless of which bone a vertex is assigned to, and then follow that
    /// bone's own animation correctly from there.
    /// </para>
    ///
    /// <para><b>Bone assignment.</b> Because the calibrated round trip reproduces rest EXACTLY for
    /// every candidate bone (that's what Calibration is for), reproduction error carries no
    /// per-vertex signal at all for picking which bone should drive a given vertex - every bone
    /// "explains" the rest pose equally well. Falls back to physical distance from the vertex to
    /// each candidate bone's own bind-pose origin instead, which at least tracks the real skeleton
    /// once animated (a vertex sticks with whichever bone actually sits closest to it), in place of
    /// the true per-vertex blend weights this project doesn't parse. Candidates in
    /// <see cref="DeformedBoneIndices"/> (this same cloth's OWN mesh-bone-deform output bones,
    /// written from simulated triangle positions later in this very Step) are excluded - treating a
    /// cloth-simulated bone as skin INPUT closes a feedback loop within a single Step (skin feeds an
    /// anchor, simulate moves it, mesh-bone-deform turns that motion back into a bone transform, and
    /// next frame's skin reads that same bone as if it were a kinematic driver) that blew up to NaN
    /// within ~20 frames in testing before this exclusion.</para>
    /// </summary>
    private (Matrix4x4[] InverseBind, int[] BoneAssignment) PrepareSkinOperator(HclObjectSpaceSkinOperator op, ushort[] subset, Vector3[] restPositions, int count, int boneCount)
    {
        var worldRefPose = Skeleton!.WorldReferencePose;

        // Object-space skinning needs, per bone, the inverse of that bone's BIND world matrix in the
        // SAME space the rest positions and the runtime bone matrices live in. That space is
        // WorldReferencePose: it is verified to agree with the real game skeleton's own bind pose to
        // 0.0000m of translation, 1.0000x of scale and under 0.04 degrees of rotation on every bone
        // of every cloth piece (DiagnoseClothSkeletonVsRealSkeleton), and PlacedActor feeds this
        // operator the real skeleton's animated world matrices.
        //
        // This deliberately does NOT use BoneFromSkinMeshTransforms. That array is the inverse bind
        // of the original authored SKIN MESH, which sits in a different space - hence the fixed,
        // bone-independent residual this class used to measure and "calibrate" away. The old formula
        // was `bfsm[b] * animatedWorld[b] * inverse(bfsm[b] * worldRefPose[b])`, i.e.
        // `bfsm * W * inverse(worldRefPose) * inverse(bfsm)` - the correct delta CONJUGATED by bfsm.
        // Conjugation preserves the identity, so at the bind pose it collapsed to the identity and
        // every rest-pose test in this project passed; but it rotates the bone's actual motion into
        // the wrong frame, so under animation cloth was displaced along the wrong axes and stretched
        // in the wrong direction. Measured by TestSkinOperatorRigidRotation: a RIGID whole-skeleton
        // rotation, which must move cloth rigidly and produce exactly zero deformation, was
        // displacing vertices by up to 0.60m. The correct delta below reproduces it exactly.
        var inverseBind = new Matrix4x4[boneCount];
        for (int b = 0; b < boneCount; b++)
        {
            int globalIdx = subset[b];
            inverseBind[b] = globalIdx < worldRefPose.Count && Matrix4x4.Invert(worldRefPose[globalIdx], out var inv)
                ? inv
                : Matrix4x4.Identity;
        }

        var boneOrigins = new Vector3[boneCount];
        var candidateOk = new bool[boneCount];
        bool anyCandidate = false;
        for (int b = 0; b < boneCount; b++)
        {
            int globalIdx = subset[b];
            boneOrigins[b] = globalIdx < worldRefPose.Count ? worldRefPose[globalIdx].Translation : Vector3.Zero;
            candidateOk[b] = !DeformedBoneIndices.Contains(globalIdx);
            anyCandidate |= candidateOk[b];
        }
        if (!anyCandidate)
        {
            Array.Fill(candidateOk, true);
        }

        var assignment = new int[count];
        for (int v = 0; v < count; v++)
        {
            Vector3 rest = restPositions[v];
            int best = 0;
            float bestDistSq = float.MaxValue;
            for (int b = 0; b < boneCount; b++)
            {
                if (!candidateOk[b]) continue;
                float d = Vector3.DistanceSquared(rest, boneOrigins[b]);
                if (d < bestDistSq)
                {
                    bestDistSq = d;
                    best = b;
                }
            }
            assignment[v] = best;
        }

        return (inverseBind, assignment);
    }

    private void ExecuteMoveParticlesOperator(HclMoveParticlesOperator op)
    {
        int simIdx = (int)op.SimClothIndex;
        int refIdx = (int)op.RefBufferIndex;

        if (simIdx >= Runtimes.Count || refIdx >= Buffers.Length) return;

        var runtime = Runtimes[simIdx];
        var refBuf = Buffers[refIdx];

        // The real hclMoveParticlesOperator's own branch - see SimClothRuntime.MoveParticleTo.
        bool fixVelocity = runtime.Data.FixedParticles.Length == 0;

        foreach (var pair in op.VertexParticlePairs)
        {
            if (pair.VertexIndex < refBuf.Length && pair.ParticleIndex < runtime.ParticleCount)
            {
                runtime.MoveParticleTo(pair.ParticleIndex, refBuf[pair.VertexIndex], fixVelocity);
            }
        }
    }

    private void ExecuteSimulateOperator(HclSimulateOperator op, float dt, ReadOnlySpan<Matrix4x4> currentSkeleton, bool enableCollision)
    {
        int simIdx = (int)op.SimClothIndex;
        if (simIdx >= Runtimes.Count) return;

        var runtime = Runtimes[simIdx];
        // Use EXACTLY the authored sub-step count. An earlier version refined this to a bounded
        // sub-step size (1/240s, so 4 sub-steps at 60fps) on the theory that the real engine's
        // WORLD-level hclWorldStepInfo::m_numWorldSubSteps means the game integrates more finely
        // than the authored 1. That is true of the real engine, but reproducing it here is actively
        // harmful, because a sub-step is not just a smaller integration step - it also runs a FULL
        // constraint solve. Running four solves per frame where Havok runs one over-converges the
        // constraint graph and drives the cloth toward its rest shape far harder than authored,
        // which reads as a piece that will not wiggle: measured on SimulationMesh_Rope_1, whose
        // local-range tethers permit 0.21-0.30m of drift, free particles were lagging their
        // kinematic reference by only 0.014m. The authored values are the calibration Havok's own
        // formulas expect (see PbdSolver's remarks on stiffness being tuned against un-normalised
        // invMass), so anything other than 1:1 makes this cloth behave unlike the game's.
        int subSteps = Math.Max(1, (int)op.SubSteps);
        int iterations = Math.Max(1, (int)op.NumberOfSolveIterations);
        float subDt = dt / subSteps;

        // Apply external wind to particle velocities
        if (Wind != Vector3.Zero)
        {
            for (int i = 0; i < runtime.ParticleCount; i++)
            {
                if (!runtime.IsFixed[i])
                {
                    runtime.Positions[i] += Wind * (0.01f * subDt);
                }
            }
        }

        var refPositions = Buffers.Length > 0 ? Buffers[0] : Array.Empty<Vector3>();

        // Anchor particles must WALK to their newly-skinned positions across the sub-steps, not jump
        // to them before the first one. The real hclSimulateOperator::executeCpu snapshots every
        // fixed particle's (previous, current) pair BEFORE its sub-step loop - previous being where
        // the anchor sat last frame and current where this frame's skinning just put it (see
        // SimClothRuntime.MoveParticleTo for how that pair is preserved) - and then, at the top of
        // each sub-step, places the anchor at lerp(previous, current, (i+1)/numSubSteps).
        //
        // Teleporting instead, which is what this loop used to do implicitly, hands the very first
        // constraint solve a link stretched by the anchor's ENTIRE frame of travel and lets it yank
        // the attached free particle by a correction sized to that whole jump. On a long chain -
        // Zelda's back hair is 28-30 particles - each yank propagates down the strand and the chain
        // gains energy every frame instead of settling, which is both the visible "cloth gets
        // brutally jerked around when animations play" and the measured 4.7-7.6x runaway growth in
        // WildRenderingSharp.Cloth.Tests' DiagnoseAllClothPiecesUnderMotion. Broad, short pieces (the cape)
        // absorb it; long strands do not.
        var fixedParticles = runtime.Data.FixedParticles;
        Vector3[]? anchorFrom = null;
        Vector3[]? anchorTo = null;
        if (subSteps > 1 && fixedParticles.Length > 0)
        {
            anchorFrom = new Vector3[fixedParticles.Length];
            anchorTo = new Vector3[fixedParticles.Length];
            for (int f = 0; f < fixedParticles.Length; f++)
            {
                int idx = fixedParticles[f];
                if ((uint)idx >= (uint)runtime.ParticleCount) continue;
                anchorFrom[f] = runtime.PrevPositions[idx];
                anchorTo[f] = runtime.Positions[idx];
            }
        }

        var collisionFrame = CollisionSolver.BeginFrame(
            runtime, runtime.Data.Collidables, currentSkeleton,
            runtime.Data.CollidableTransformIndices, runtime.Data.CollidableTransformOffsets,
            Skeleton?.WorldReferencePose, dt);

        for (int step = 0; step < subSteps; step++)
        {
            // Havok advances the implicitly-driven collidables once at the start of each substep,
            // before integrating particles. Their point velocity is retained for contact friction.
            collisionFrame.Advance(subDt);

            if (anchorFrom != null && anchorTo != null)
            {
                float blend = (step + 1) / (float)subSteps;
                for (int f = 0; f < fixedParticles.Length; f++)
                {
                    int idx = fixedParticles[f];
                    if ((uint)idx >= (uint)runtime.ParticleCount) continue;
                    Vector3 interpolated = Vector3.Lerp(anchorFrom[f], anchorTo[f], blend);
                    runtime.Positions[idx] = interpolated;
                    runtime.PrevPositions[idx] = interpolated;
                }
            }

            // Verlet predict
            PbdSolver.Integrate(runtime, subDt, GravityScale);

            // Solver iterations. When the piece authors an execution order, follow the real
            // hclSimulateOperator::_solveTaskOrderedCpu: walk that list, treating -1 as "collide
            // here" and anything else as an index into this sim cloth's own constraint-set array.
            // A set legitimately appears more than once (Zelda's data applies its Standard Links up
            // to three times per iteration) and sets can be scheduled AFTER collision. Only when the
            // list is empty does the engine fall back to _solveTaskCpu's "every set once, in array
            // order, then collide", which is what this used to do unconditionally.
            var sets = runtime.Data.ConstraintSets;
            var execution = op.ConstraintExecution;

            for (int iter = 0; iter < iterations; iter++)
            {
                if (execution.Length > 0)
                {
                    foreach (int entry in execution)
                    {
                        if (entry == -1)
                        {
                            if (enableCollision)
                                collisionFrame.Solve(op.UseAllInstanceCollidables,
                                    op.InstanceCollidablesUsed, subDt);
                        }
                        else if ((uint)entry < (uint)sets.Length)
                        {
                            PbdSolver.SolveConstraintSet(runtime, sets[entry], refPositions);
                        }
                    }
                }
                else
                {
                    PbdSolver.SolveConstraints(runtime, sets, refPositions);

                    if (enableCollision)
                        collisionFrame.Solve(op.UseAllInstanceCollidables,
                            op.InstanceCollidablesUsed, subDt);
                }
            }
        }
        collisionFrame.Commit();

        // Copy current simulated positions into buffer 1 (SimCurrentBuf) if allocated
        if (Buffers.Length > 1)
        {
            int copyCount = Math.Min(Buffers[1].Length, runtime.Positions.Length);
            Array.Copy(runtime.Positions, Buffers[1], copyCount);
        }
    }

    private void ExecuteMeshBoneOperator(HclSimpleMeshBoneDeformOperator op)
    {
        int inBufIdx = (int)op.InputBufferIndex;
        if (Runtimes.Count == 0) return;

        var runtime = Runtimes[0];
        var tris = runtime.Data.TriangleIndices;

        MeshBoneSolver.Execute(op, runtime, tris, SkeletonTransforms);
    }
}
