using KingRenderingSharp.Graphics.Contracts;
using KingRenderingSharp.Pipeline.Drawing;
using KingRenderingSharp.Pipeline.Shadows;
using KingRenderingSharp.Rendering.Cameras;
using KingRenderingSharp.Rendering.Lighting;

namespace KingRenderingSharp.Pipeline;

/// <summary>Everything needed to render one frame.</summary>
public sealed record FrameRequest(
    Camera Camera, LightingContext Lighting, IFrameEnvironment Environment, IReadOnlyList<ActorRenderInput> Actors,
    float AoRadius, float ShadowBias,
    (int ActorIndex, int ShapeIndex)? Highlight = null,
    IReadOnlyList<InstanceBatch>? Instances = null,
    ShadowFocus? ShadowFocus = null,
    IReadOnlyList<ShadowFocus>? ShadowCascades = null,
    IReadOnlyList<float>? ShadowSliceEnds = null);
