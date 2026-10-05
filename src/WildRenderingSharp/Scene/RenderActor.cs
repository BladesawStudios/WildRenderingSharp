using System.Numerics;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Cloth.Simulation;
using WildRenderingSharp.Cloth.Simulation.HelperBone;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Scene;

/// <summary>
/// One instance of a loaded model placed in the scene. Owns its own placement transform AND its
/// own complete set of animation channels (skeletal/shader-param/texture-SRT/texture-pattern) and
/// physics (helper bones, Havok cloth), so two placed actors animate fully independently.
/// </summary>
/// <remarks>
/// <para>
/// Does NOT own a <see cref="DeferredPipeline"/> - one shared pipeline draws every placed actor's
/// shapes together (see <see cref="DeferredPipeline.SetScene"/>). Nor does it own any GPU-side
/// skinning bytes: <see cref="DeferredPipeline.RenderFrame"/> rebuilds this actor's own
/// bone-palette/ShpMtx buffer fresh every frame from <see cref="ToRenderInput"/> into a transient
/// <see cref="ActorDrawGroup"/>, rebound to the shared <c>_Mtx</c>/<c>ShpMtx</c> binding points
/// immediately before this actor's own shapes draw - the real compiled game shaders read bone
/// transforms from those two GLOBAL, frame-shared UBO binding points with no per-draw instance
/// addressing at all, so every actor needs ITS OWN buffer bound there right before its own draw calls.
/// </para>
/// <para>
/// The world is Z-up. A BFRES model is authored Y-up, which is why <see cref="Pitch"/> defaults to
/// a quarter turn: that is what stands a model upright. A host with its own Y-up world can either
/// keep that default and convert its camera (see <see cref="Hosting.YUpWorld"/>), or supply its
/// own matrix through <see cref="TransformOverride"/>.
/// </para>
/// <para>
/// Not sealed: a host commonly wants to hang its own editor state (a gizmo interface, a display
/// name, an icon framing) off the same object.
/// </para>
/// </remarks>
public class RenderActor : IDisposable
{
    public required LoadedModel Model { get; init; }

    /// <summary>The resolved model name it was loaded as (its cache directory name).</summary>
    public required string ModelName { get; init; }

    public string Name { get; set; } = "Actor";

    /// <summary>Placement rotation - yaw about Z then pitch about local X then roll about the actor's own forward axis, about the model's own bounds centre. Pitch defaults to 90 degrees, matching the "lying down" BFRES import convention every model needs a quarter-turn to stand naturally in this Z-up world.</summary>
    public Vector3 Position { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; } = MathF.PI / 2f;
    public float Roll { get; set; }
    public Vector3 Scale { get; set; } = Vector3.One;

    /// <summary>False leaves the actor out of the frame without unloading it.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// When set, replaces the Position/Yaw/Pitch/Roll/Scale placement entirely: the GPU "rows"
    /// convention <see cref="TransformRows"/> returns (row i holds the matrix's i-th row, with the
    /// translation in each row's W - the layout <see cref="EulerRotation"/> builds). Build one from
    /// a row-vector <see cref="Matrix4x4"/> with <see cref="RowsFromMatrix"/>.
    /// </summary>
    public Vector4[]? TransformOverride { get; set; }

    /// <summary>
    /// When set, used as this actor's posed skeleton instead of evaluating its own animation
    /// channels and physics - for a host that already poses the same BFRES skeleton itself. One
    /// object-space bone matrix per skeleton bone, in the model's own bone order, row-vector
    /// convention (the shape <see cref="SkeletonPose.BindPoseWorldMatrices"/> returns).
    /// </summary>
    public Matrix4x4[]? ExternalPose { get; set; }

    /// <summary>
    /// The actor's own placement transform - what every render pass actually needs. Rotation AND
    /// scale pivot about the model's own bounds centre (so rotating/scaling in place doesn't also
    /// shift the object); <see cref="Position"/> is then added as an INDEPENDENT world-space
    /// translation on top, not folded into the pivot itself - folding it in would make Position's
    /// effect a function of the current rotation instead of a plain additive move.
    /// </summary>
    public Vector4[] TransformRows() =>
        TransformOverride ?? EulerRotation.MakeYawPitchRollScaleAboutPivot(Yaw, Pitch, Roll, Scale, Model.BoundsCenter, Position);

    /// <summary>A row-vector <see cref="Matrix4x4"/> (<c>Vector3.Transform(v, m)</c>) as the GPU rows <see cref="TransformOverride"/> takes.</summary>
    public static Vector4[] RowsFromMatrix(Matrix4x4 m) =>
    [
        new(m.M11, m.M21, m.M31, m.M41),
        new(m.M12, m.M22, m.M32, m.M42),
        new(m.M13, m.M23, m.M33, m.M43),
    ];

    /// <summary>The inverse of <see cref="RowsFromMatrix"/>.</summary>
    public static Matrix4x4 MatrixFromRows(ReadOnlySpan<Vector4> r) => new(
        r[0].X, r[1].X, r[2].X, 0f,
        r[0].Y, r[1].Y, r[2].Y, 0f,
        r[0].Z, r[1].Z, r[2].Z, 0f,
        r[0].W, r[1].W, r[2].W, 1f);

    /// <summary>The model's visible centre in world space - the point <see cref="TransformRows"/> rotates about, invariant under that rotation.</summary>
    public Vector3 WorldCenter => TransformOverride is { } rows
        ? Vector3.Transform(Model.BoundsCenter, MatrixFromRows(rows))
        : Position + Model.BoundsCenter;

    public Vector3 Forward() => EulerRotation.YawPitchRollBasis(Yaw, Pitch, Roll).Forward;

    public (Vector3 Forward, Vector3 Right, Vector3 Up) Basis()
    {
        var (right, up, forward) = EulerRotation.YawPitchRollBasis(Yaw, Pitch, Roll);
        return (forward, right, up);
    }

    // ---- Per-actor animation state ----

    public SkeletalAnimManifest? SkeletalClip { get; private set; }
    public AnimSlot<SkeletalAnimManifest>? Skeletal { get; private set; }
    public AnimChannel<MaterialAnimManifest> ShaderParam { get; } = new();
    public AnimChannel<MaterialAnimManifest> TextureSrt { get; } = new();
    public AnimChannel<TexturePatternAnimManifest> TexturePattern { get; } = new(stepped: true);

    public IEnumerable<AnimSlot<MaterialAnimManifest>> MaterialSlots => ShaderParam.Slots.Concat(TextureSrt.Slots);

    public void PlaySkeletal(SkeletalAnimManifest clip)
    {
        SkeletalClip = clip;
        Skeletal = new AnimSlot<SkeletalAnimManifest>(clip, stepped: false);
    }

    public void StopSkeletal()
    {
        SkeletalClip = null;
        Skeletal = null;
    }

    public void ResetAnimations()
    {
        StopSkeletal();
        ShaderParam.Clear();
        TextureSrt.Clear();
        TexturePattern.Clear();
    }

    /// <summary>BFRES skeletal anims don't carry their own playback rate in the exported manifest - 30 is the standard TotK/g3d anim frame rate.</summary>
    public const float DefaultFramesPerSecond = 30f;

    /// <returns>True if anything actually moved this frame - callers use this to decide whether the frame needs redrawing.</returns>
    public bool AdvanceAnimation(float deltaSeconds, float framesPerSecond = DefaultFramesPerSecond)
    {
        bool moved = Skeletal?.Advance(deltaSeconds, framesPerSecond) ?? false;
        moved |= ShaderParam.Advance(deltaSeconds, framesPerSecond);
        moved |= TextureSrt.Advance(deltaSeconds, framesPerSecond);
        moved |= TexturePattern.Advance(deltaSeconds, framesPerSecond);
        if (ClothEnabled && ClothInstances.Count > 0)
            moved = true;
        return moved;
    }

    /// <summary>
    /// Applies this actor's texture-pattern and material animations to its model: a pattern anim
    /// re-points sampler bindings and a material anim rewrites uniform blocks, both of which every
    /// pass then picks up while drawing this actor's shapes. Needs the GL context. Only the skeletal
    /// pose feeds a per-frame UBO; that one goes through <see cref="ToRenderInput"/>.
    /// </summary>
    public void ApplyMaterialAnimations(Silk.NET.OpenGL.GL gl)
    {
        var patternAnims = TexturePattern.Slots
            .Select(s => new TexturePatternPose.Playing(s.Clip, s.Frame)).ToList();
        if (patternAnims.Count > 0)
            TexturePatternPose.Apply(Model, patternAnims, Model.Textures);
        else
            TexturePatternPose.Clear(Model);

        var materialAnims = MaterialSlots
            .Select(s => new MaterialAnimPose.Playing(s.Clip, s.Frame)).ToList();
        if (materialAnims.Count > 0)
            MaterialAnimPose.Apply(gl, Model, materialAnims);
        else
            MaterialAnimPose.Clear(gl, Model);
    }

    /// <summary>
    /// This actor's input to one <see cref="DeferredPipeline.RenderFrame"/> - its placement and its
    /// posed skeleton (see <see cref="EvaluatePosedSkeleton"/> for <paramref name="frameId"/>).
    /// </summary>
    public ActorRenderInput ToRenderInput(float deltaSeconds, ulong frameId, Vector3 sceneWind = default) =>
        new(Model, TransformRows(), EvaluatePosedSkeleton(deltaSeconds, frameId, sceneWind));

    /// <summary>Every visible actor's render input for one frame.</summary>
    public static List<ActorRenderInput> BuildRenderInputs(IEnumerable<RenderActor> actors, float deltaSeconds, ulong frameId, Vector3 sceneWind = default) =>
        actors.Where(a => a.Visible).Select(a => a.ToRenderInput(deltaSeconds, frameId, sceneWind)).ToList();

    /// <summary>The union of every actor's world-space bounding sphere.</summary>
    public static (Vector3 Center, float Radius) CombinedBounds(IEnumerable<RenderActor> actors)
    {
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        bool any = false;
        foreach (var a in actors)
        {
            var c = a.WorldCenter;
            var r = new Vector3(a.Model.BoundsRadius * MathF.Max(a.Scale.X, MathF.Max(a.Scale.Y, a.Scale.Z)));
            lo = Vector3.Min(lo, c - r);
            hi = Vector3.Max(hi, c + r);
            any = true;
        }
        if (!any)
            return (Vector3.Zero, 1f);
        return ((lo + hi) * 0.5f, MathF.Max((hi - lo).Length() * 0.5f, 0.001f));
    }

    // ---- Physics (Phive Helper Bones & Havok Cloth) ----

    private HelperBoneSolver? _helperBoneSolver;
    private int[]? _hbToSkel;
    private Matrix4x4[]? _hbTransforms;

    private readonly List<ClothInstance> _clothInstances = new();
    private readonly List<int[]> _clothToSkel = new();
    private bool _physicsInitialized;

    public HelperBoneSolver? HelperBoneSolver
    {
        get
        {
            EnsurePhysicsInitialized();
            return _helperBoneSolver;
        }
    }

    public IReadOnlyList<ClothInstance> ClothInstances
    {
        get
        {
            EnsurePhysicsInitialized();
            return _clothInstances;
        }
    }

    public bool HelperBonesEnabled { get; set; } = true;
    public bool ClothEnabled { get; set; } = true;

    /// <summary>
    /// Multiplies authored gravity for every cloth piece on this actor - see
    /// <c>WildRenderingSharp.Cloth.Simulation.Solvers.PbdSolver.Integrate</c>'s own remarks for why
    /// this is an exposed artistic knob rather than a value read from data: every real cloth piece
    /// checked authors genuine real-world <c>-9.81</c> gravity with no wind/drag action alongside
    /// it, so a TotK cloth piece reading as "lighter" than that in-engine comes from a world-level
    /// override this file format has no room to express. Default &lt; 1 reflects that perception;
    /// 1.0 reproduces the authored value exactly.
    /// </summary>
    public float ClothGravityScale { get; set; } = 0.6f;

    private Matrix4x4? _prevActorWorld;

    public void ResetPhysics()
    {
        EnsurePhysicsInitialized();
        foreach (var inst in _clothInstances)
        {
            inst.Reset();
        }
        _prevActorWorld = null;
    }

    /// <summary>
    /// <see cref="TransformRows"/> as a row-vector-convention <see cref="Matrix4x4"/> (i.e. one
    /// usable with <c>Vector3.Transform(v, M)</c> the way every other matrix in the cloth/skeleton
    /// math is) - TransformRows itself is GL-style "M * column-vector" (see its own row layout in
    /// EulerRotation), so building straight off its rows would silently transpose the rotation.
    /// Used only to feed cloth's reference-frame lag (see EvaluatePosedSkeleton).
    /// </summary>
    private Matrix4x4 RowVectorWorldTransform() => MatrixFromRows(TransformRows());

    private void EnsurePhysicsInitialized()
    {
        if (_physicsInitialized) return;
        _physicsInitialized = true;

        if (Model.Skeleton is not { } skel) return;

        var skelBoneMap = new Dictionary<string, int>(skel.Bones.Count, StringComparer.Ordinal);
        for (int i = 0; i < skel.Bones.Count; i++)
        {
            skelBoneMap[skel.Bones[i].Name] = i;
        }

        // Helper Bones
        if (Model.HelperBone is { } hbData)
        {
            _helperBoneSolver = new HelperBoneSolver(hbData);
            _hbToSkel = new int[hbData.Bones.Count];
            for (int i = 0; i < hbData.Bones.Count; i++)
            {
                _hbToSkel[i] = skelBoneMap.TryGetValue(hbData.Bones[i], out int idx) ? idx : -1;
            }
            _hbTransforms = new Matrix4x4[hbData.Bones.Count];
        }

        // Havok Cloth
        if (Model.ClothContainer is { } clothContainer)
        {
            Console.WriteLine($"[RenderActor] '{Name}': {clothContainer.ClothDatas.Count} cloth pieces.");
            var animSkeletons = Model.ClothAnimContainer?.Skeletons;
            for (int c = 0; c < clothContainer.ClothDatas.Count; c++)
            {
                var clothData = clothContainer.ClothDatas[c];
                string targetSkelName = clothData.TransformSetDefinitions.Count > 0
                    ? clothData.TransformSetDefinitions[0].Name
                    : clothData.Name;

                var hkaSkel = animSkeletons?.FirstOrDefault(s => s.Name == targetSkelName || s.Name == clothData.Name || s.Name == "cloth_skeleton_" + clothData.Name)
                              ?? (animSkeletons != null && animSkeletons.Count > 0 ? animSkeletons[Math.Min(c, animSkeletons.Count - 1)] : null);

                // Each sim cloth's m_perInstanceCollidables is already its authored selection from
                // the container's exported collider pool (Mant selects 16 of Zelda's 30). Do not
                // add the whole container again here.
                var instance = new ClothInstance(clothData, hkaSkel);
                _clothInstances.Add(instance);

                if (hkaSkel != null)
                {
                    int[] map = new int[hkaSkel.Bones.Count];
                    for (int b = 0; b < hkaSkel.Bones.Count; b++)
                    {
                        map[b] = skelBoneMap.TryGetValue(hkaSkel.Bones[b].Name, out int idx) ? idx : -1;
                    }
                    _clothToSkel.Add(map);
                }
                else
                {
                    _clothToSkel.Add(Array.Empty<int>());
                }
            }
        }
    }

    private ulong? _lastPosedFrameId;
    private Matrix4x4[]? _lastPosedResult;

    /// <summary>
    /// Evaluates the posed skeleton with skeletal animations, helper bones, and cloth simulation
    /// applied. Returns null if the model has no skeleton or is purely static at bind pose with no
    /// active physics (the pipeline then uses the bind pose). <see cref="ExternalPose"/>, when set,
    /// is returned as-is.
    /// </summary>
    /// <param name="frameId">
    /// A counter the host increments once per real rendered frame. Cloth/helper-bone physics is
    /// stateful and must step exactly once per real render regardless of how many times (main view,
    /// a picture-in-picture preview, an export) this actor's pose gets asked for within that frame;
    /// a repeat call with the same id short-circuits to the frame's already-computed result instead
    /// of stepping physics again - stepping it twice silently runs the simulation at double speed.
    /// </param>
    public Matrix4x4[]? EvaluatePosedSkeleton(float dt, ulong frameId, Vector3 sceneWind)
    {
        if (ExternalPose is { } external)
            return external;

        if (Model.Skeleton is not { } skel)
            return null;

        bool hasActivePhysics = (HelperBonesEnabled && Model.HelperBone != null) ||
                                (ClothEnabled && Model.ClothContainer != null);

        if (Skeletal == null && !hasActivePhysics)
            return null;

        if (_lastPosedFrameId == frameId)
            return _lastPosedResult;

        EnsurePhysicsInitialized();

        Matrix4x4[] world = Skeletal is { } skeletal
            ? SkeletonPose.AnimatedWorldMatrices(skel, skeletal.Clip, skeletal.Frame)
            : SkeletonPose.BindPoseWorldMatrices(skel);

        // 1. Evaluate Helper Bones
        if (HelperBonesEnabled && _helperBoneSolver != null && _hbToSkel != null && _hbTransforms != null)
        {
            for (int i = 0; i < _hbToSkel.Length; i++)
            {
                int skelIdx = _hbToSkel[i];
                _hbTransforms[i] = (skelIdx >= 0 && skelIdx < world.Length) ? world[skelIdx] : Matrix4x4.Identity;
            }

            _helperBoneSolver.Solve(_hbTransforms);

            foreach (var driven in _helperBoneSolver.Data.DrivenBones)
            {
                int hbIdx = driven.BoneId;
                if (hbIdx >= 0 && hbIdx < _hbToSkel.Length)
                {
                    int skelIdx = _hbToSkel[hbIdx];
                    if (skelIdx >= 0 && skelIdx < world.Length)
                    {
                        world[skelIdx] = _hbTransforms[hbIdx];
                    }
                }
            }
        }

        // 2. Evaluate Havok Cloth
        if (ClothEnabled && _clothInstances.Count > 0)
        {
            float clampedDt = Math.Clamp(dt, 0.001f, 0.05f);

            // Cloth simulates in object space (see ClothInstance's own remarks), so whole-actor
            // motion (dragging/rotating the actor) never touches a single bone matrix and would
            // otherwise be perfectly invisible to it - the actor would just drag the cloth along
            // rigidly with zero reaction. Re-deriving this frame's placement delta and handing it
            // to every cloth instance is what makes free cloth lag behind the body instead.
            Matrix4x4 curActorWorld = RowVectorWorldTransform();
            Matrix4x4 referenceFrameDelta = Matrix4x4.Identity;
            Vector3 localWind = sceneWind;
            if (sceneWind.LengthSquared() > 1e-8f && Matrix4x4.Invert(curActorWorld, out var worldToActor))
            {
                Vector3 transformed = Vector3.TransformNormal(sceneWind, worldToActor);
                if (transformed.LengthSquared() > 1e-8f)
                    localWind = Vector3.Normalize(transformed) * sceneWind.Length();
            }
            if (_prevActorWorld is { } prevWorld && Matrix4x4.Invert(curActorWorld, out var invCur))
            {
                Matrix4x4 delta = prevWorld * invCur;

                // A real per-frame actor motion is small; a multi-metre jump or near-180-degree
                // spin in one frame can only be a discontinuity (a gizmo snapping to a drag start
                // point, the actor being freshly placed, a scene reset) rather than genuine motion
                // to lag behind. Feeding that straight into the lag term would inject a single-frame
                // velocity spike large enough to fling free particles away, reading as instant
                // explosion. Falling back to Identity for a jump like that just means cloth snaps to
                // match the new placement instantly, with no lag - the correct behaviour for a
                // discontinuity, since there's no real motion to lag behind.
                if (Matrix4x4.Decompose(delta, out _, out Quaternion deltaRot, out Vector3 deltaTrans))
                {
                    float angle = 2f * MathF.Acos(Math.Clamp(MathF.Abs(deltaRot.W), 0f, 1f));
                    if (deltaTrans.Length() <= 1f && angle <= MathF.PI / 2f)
                        referenceFrameDelta = delta;
                }
            }
            _prevActorWorld = curActorWorld;

            for (int c = 0; c < _clothInstances.Count; c++)
            {
                var instance = _clothInstances[c];
                var map = _clothToSkel[c];

                // PBD runs in actor-object space. Transform the one scene/world force into this
                // actor's local frame so differently-rotated actors still visibly experience the
                // SAME wind direction and strength, instead of each treating +X as its own +X.
                instance.Wind = localWind;
                instance.GravityScale = ClothGravityScale;

                // A cloth-skeleton bone whose name isn't found in the actor's real skeleton (map[b]
                // < 0) has no sensible current pose available at all - falling back to raw Identity
                // would snap it to the world origin, an arbitrarily huge, sudden displacement that
                // the skin operator would feed into the simulation as instant, catastrophic
                // stretching. Falling back to the bone's own last-known transform (initialised to
                // its bind pose by ClothInstance.Reset and never touched again for a permanently-
                // unmapped bone) keeps it exactly where it was instead.
                var inputSkel = new Matrix4x4[instance.SkeletonTransforms.Length];
                for (int b = 0; b < map.Length && b < inputSkel.Length; b++)
                {
                    int skelIdx = map[b];
                    inputSkel[b] = (skelIdx >= 0 && skelIdx < world.Length) ? world[skelIdx] : instance.SkeletonTransforms[b];
                }

                instance.Step(clampedDt, inputSkel, referenceFrameDelta);

                if (instance.DeformedBoneIndices.Count > 0)
                {
                    foreach (int b in instance.DeformedBoneIndices)
                    {
                        if (b >= 0 && b < map.Length && b < instance.SkeletonTransforms.Length)
                        {
                            int skelIdx = map[b];
                            if (skelIdx >= 0 && skelIdx < world.Length)
                            {
                                world[skelIdx] = instance.SkeletonTransforms[b];
                            }
                        }
                    }
                }
                else
                {
                    for (int b = 0; b < map.Length && b < instance.SkeletonTransforms.Length; b++)
                    {
                        int skelIdx = map[b];
                        if (skelIdx >= 0 && skelIdx < world.Length)
                        {
                            world[skelIdx] = instance.SkeletonTransforms[b];
                        }
                    }
                }
            }
        }

        _lastPosedFrameId = frameId;
        _lastPosedResult = world;
        return world;
    }

    public virtual void Dispose()
    {
        Model.Dispose();
        GC.SuppressFinalize(this);
    }
}
