using System.Numerics;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Scene;

/// <summary>
/// One instance of a loaded model placed in the scene, with its own placement and its own animation channels (skeletal,
/// shader-param, texture-SRT, texture-pattern), so placed actors animate independently.
/// </summary>
/// <remarks>
/// <para>
/// It owns no <see cref="DeferredPipeline"/> (one shared pipeline draws every actor) and no skinning buffers: the pipeline
/// builds this actor's uniforms fresh each frame from <see cref="ToRenderInput"/> into an <see cref="ActorDrawGroup"/>, bound
/// right before its shapes draw, because the game's shaders read bones from shared binding points with no per-draw instance
/// addressing.
/// </para>
/// <para>
/// The world is Z-up while a BFRES model is authored Y-up, so <see cref="Pitch"/> defaults to a quarter turn to stand a model
/// upright. A host with its own Y-up world can keep that default and convert its camera (see <see cref="Hosting.YUpWorld"/>),
/// or supply its own matrix through <see cref="TransformOverride"/>.
/// </para>
/// <para>Not sealed: a host commonly hangs its own editor state off the same object.</para>
/// <para>
/// Physics is not the renderer's. A host running Havok Cloth or Phive Helper Bones (HkxSimSharp, HkxHbSharp) overrides
/// <see cref="ModifiesPose"/> and <see cref="ModifyPose"/> to write what they drive into the pose; cloth reaches the screen
/// as a handful of bone matrices.
/// </para>
/// </remarks>
public class RenderActor : IDisposable
{
    public required LoadedModel Model { get; init; }

    /// <summary>The resolved model name it was loaded as (its cache directory name).</summary>
    public required string ModelName { get; init; }

    public string Name { get; set; } = "Actor";

    /// <summary>
    /// Placement rotation - yaw about Z then pitch about local X then roll about the actor's own forward axis, about the model's
    /// own bounds centre. Pitch defaults to 90 degrees, matching the "lying down" BFRES import convention every model needs a
    /// quarter-turn to stand naturally in this Z-up world.
    /// </summary>
    public Vector3 Position { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; } = MathF.PI / 2f;
    public float Roll { get; set; }
    public Vector3 Scale { get; set; } = Vector3.One;

    /// <summary>False leaves the actor out of the frame without unloading it.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// When set, replaces the Position/Yaw/Pitch/Roll/Scale placement entirely: the GPU rows <see cref="TransformRows"/> returns
    /// (row i is the matrix's i-th row, translation in each row's W; the layout <see cref="EulerRotation"/> builds). Build one from
    /// a row-vector <see cref="Matrix4x4"/> with <see cref="RowsFromMatrix"/>.
    /// </summary>
    public Vector4[]? TransformOverride { get; set; }

    /// <summary>
    /// When set, used as this actor's posed skeleton instead of evaluating its own animation channels - for a host that already
    /// poses the same BFRES skeleton itself. One object-space bone matrix per skeleton bone, in the model's own bone order,
    /// row-vector convention (the shape <see cref="SkeletonPose.BindPoseWorldMatrices"/> returns).
    /// </summary>
    public Matrix4x4[]? ExternalPose { get; set; }

    /// <summary>
    /// The placement transform every pass needs. Rotation and scale pivot about the model's bounds centre so rotating in place does
    /// not shift the object; <see cref="Position"/> is then added as an independent world-space translation, so its effect does not
    /// depend on the rotation.
    /// </summary>
    public Vector4[] TransformRows() =>
        TransformOverride ?? EulerRotation.MakeYawPitchRollScaleAboutPivot(Yaw, Pitch, Roll, Scale, Model.BoundsCenter, Position);

    /// <summary>
    /// A row-vector <see cref="Matrix4x4"/> (<c>Vector3.Transform(v, m)</c>) as the GPU rows <see cref="TransformOverride"/> takes.
    /// </summary>
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

    /// <summary>
    /// The model's visible centre in world space - the point <see cref="TransformRows"/> rotates about, invariant under that
    /// rotation.
    /// </summary>
    public Vector3 WorldCenter => TransformOverride is { } rows
        ? Vector3.Transform(Model.BoundsCenter, MatrixFromRows(rows))
        : Position + Model.BoundsCenter;

    public Vector3 Forward() => EulerRotation.YawPitchRollBasis(Yaw, Pitch, Roll).Forward;

    public (Vector3 Forward, Vector3 Right, Vector3 Up) Basis()
    {
        var (right, up, forward) = EulerRotation.YawPitchRollBasis(Yaw, Pitch, Roll);
        return (forward, right, up);
    }

    // Per-actor animation state.

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

    /// <summary>The exported manifest carries no playback rate for BFRES skeletal anims; 30 is the standard g3d animation frame rate.</summary>
    public const float DefaultFramesPerSecond = 30f;

    public virtual bool AdvanceAnimation(float deltaSeconds, float framesPerSecond = DefaultFramesPerSecond)
    {
        bool moved = Skeletal?.Advance(deltaSeconds, framesPerSecond) ?? false;
        moved |= ShaderParam.Advance(deltaSeconds, framesPerSecond);
        moved |= TextureSrt.Advance(deltaSeconds, framesPerSecond);
        moved |= TexturePattern.Advance(deltaSeconds, framesPerSecond);
        return moved;
    }

    /// <summary>
    /// True while <see cref="ModifyPose"/> has work to do, such as an enabled cloth or helper-bone rig. Without it an actor with no
    /// skeletal clip draws in its bind pose and the pose is never evaluated.
    /// </summary>
    protected virtual bool ModifiesPose => false;

    /// <summary>
    /// Where a host's physics goes: called once per frame id with the animated pose (object-space bone matrices in the model's bone
    /// order, row-vector), to be overwritten in place with whatever the host's simulation drives. Runs only while <see
    /// cref="ModifiesPose"/>.
    /// </summary>
    protected virtual void ModifyPose(Matrix4x4[] world, float deltaSeconds) { }

    /// <summary>
    /// Applies this actor's texture-pattern and material animations to its model; every pass then picks them up while drawing its
    /// shapes. Needs the GL context.
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
    /// This actor's input to one <see cref="DeferredPipeline.RenderFrame"/> - its placement and its posed skeleton (see <see
    /// cref="EvaluatePosedSkeleton"/> for <paramref name="frameId"/>).
    /// </summary>
    public ActorRenderInput ToRenderInput(float deltaSeconds, ulong frameId) =>
        new(Model, TransformRows(), EvaluatePosedSkeleton(deltaSeconds, frameId));

    /// <summary>Every visible actor's render input for one frame.</summary>
    public static List<ActorRenderInput> BuildRenderInputs(IEnumerable<RenderActor> actors, float deltaSeconds, ulong frameId) =>
        actors.Where(a => a.Visible).Select(a => a.ToRenderInput(deltaSeconds, frameId)).ToList();

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

    private ulong? _lastPosedFrameId;
    private Matrix4x4[]? _lastPosedResult;

    /// <summary>
    /// Evaluates the posed skeleton: the skeletal clip (or bind pose), then whatever <see cref="ModifyPose"/> writes over it. Null
    /// if the model has no skeleton or nothing moves it (the pipeline then uses the bind pose).
    /// </summary>
    public Matrix4x4[]? EvaluatePosedSkeleton(float dt, ulong frameId)
    {
        if (ExternalPose is { } external)
            return external;

        if (Model.Skeleton is not { } skel)
            return null;

        bool modifies = ModifiesPose;
        if (Skeletal == null && !modifies)
            return null;

        if (_lastPosedFrameId == frameId)
            return _lastPosedResult;

        Matrix4x4[] world = Skeletal is { } skeletal
            ? SkeletonPose.AnimatedWorldMatrices(skel, skeletal.Clip, skeletal.Frame)
            : SkeletonPose.BindPoseWorldMatrices(skel);

        if (modifies)
            ModifyPose(world, dt);

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
