using WildRenderingSharp.Graphics;
using System.Numerics;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Scene;

/// <summary>
/// One instance of a loaded model placed in the scene, with its own placement and its own animation channels (skeletal,
/// shader-param, texture-SRT, texture-pattern), so placed actors animate independently.
/// </summary>
public class RenderActor : IDisposable
{
    public required LoadedModel Model { get; init; }

    public required string ModelName { get; init; }

    public string Name { get; set; } = "Actor";

    public Vector3 Position { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float Roll { get; set; }
    public Vector3 Scale { get; set; } = Vector3.One;

    public bool Visible { get; set; } = true;

    public Vector4[]? TransformOverride { get; set; }

    public Matrix4x4[]? ExternalPose { get; set; }

    public Vector4[] TransformRows() =>
        TransformOverride ?? EulerRotation.MakeYawPitchRollScaleAboutPivot(Yaw, Pitch, Roll, Scale, Model.BoundsCenter, Position);


    public Vector3 WorldCenter => TransformOverride is { } rows
        ? Vector3.Transform(Model.BoundsCenter, GpuMatrix.FromRows(rows))
        : Position + Model.BoundsCenter;

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

    public const float DefaultFramesPerSecond = 30f;

    public virtual bool AdvanceAnimation(float deltaSeconds, float framesPerSecond = DefaultFramesPerSecond)
    {
        bool moved = Skeletal?.Advance(deltaSeconds, framesPerSecond) ?? false;
        moved |= ShaderParam.Advance(deltaSeconds, framesPerSecond);
        moved |= TextureSrt.Advance(deltaSeconds, framesPerSecond);
        moved |= TexturePattern.Advance(deltaSeconds, framesPerSecond);
        return moved;
    }

    protected virtual bool ModifiesPose => false;

    protected virtual void ModifyPose(Matrix4x4[] world, float deltaSeconds) { }

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

    public ActorRenderInput ToRenderInput(float deltaSeconds, ulong frameId) =>
        new(Model, TransformRows(), EvaluatePosedSkeleton(deltaSeconds, frameId));

    public static List<ActorRenderInput> BuildRenderInputs(IEnumerable<RenderActor> actors, float deltaSeconds, ulong frameId) =>
        actors.Where(a => a.Visible).Select(a => a.ToRenderInput(deltaSeconds, frameId)).ToList();

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
