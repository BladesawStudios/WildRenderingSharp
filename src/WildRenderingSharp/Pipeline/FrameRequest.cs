using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Pipeline;

/// <summary>Everything needed to render one frame.</summary>
public sealed record FrameRequest(
    Camera Camera, LightingContext Lighting, IFrameEnvironment Environment, IReadOnlyList<ActorRenderInput> Actors,
    float AoRadius, float ShadowBias,
    (int ActorIndex, int ShapeIndex)? Highlight = null,
    IReadOnlyList<InstanceBatch>? Instances = null,
    ShadowFocus? ShadowFocus = null,
    IReadOnlyList<ShadowFocus>? ShadowCascades = null);
