using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Contracts;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Shadows;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Rendering.Cameras;
using WildRenderingSharp.Rendering.Lighting;

namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>
/// One frame's request and targets, and the two results every game's stages share: <see cref="Setup"/> from the setup stage and
/// <see cref="Shadow"/> from the shadow stage. What a game's own stages hand each other belongs to that game's stages.
/// </summary>
internal sealed class FrameContext(FrameRequest request, RenderTargets targets, ShadowCache shadowCache, GpuTexture? shadowMapOverride)
{
    FrameSetup? _setup;
    ShadowResult? _shadow;

    public FrameRequest Request { get; } = request;

    public RenderTargets Targets { get; } = targets;

    public ShadowCache ShadowCache { get; } = shadowCache;

    public GpuTexture? ShadowMapOverride { get; } = shadowMapOverride;

    public bool SnapshotStages { get; init; }

    public FrameStats Stats { get; } = new();

    public Camera Camera => Request.Camera;

    public LightingContext Lighting => Request.Lighting;

    public IFrameEnvironment Environment => Request.Environment;

    public IReadOnlyList<InstanceBatch> Instances { get; } = request.Instances ?? [];

    public FrameSetup Setup
    {
        get => _setup ?? throw new InvalidOperationException("The frame setup stage has not run.");
        set => _setup = value;
    }

    public ShadowResult Shadow
    {
        get => _shadow ?? throw new InvalidOperationException("No shadow stage has run.");
        set => _shadow = value;
    }

    public FrameResult Result => new(Targets.Ldr, Targets.Final, Targets.GBuffer[1], Targets.GBuffer[3], Targets.PreShadow, Targets.PreMisc, Targets.PassId);
}
