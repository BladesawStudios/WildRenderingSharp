using KingRenderingSharp.Content.Graphics.Contracts;
using KingRenderingSharp.Graph.Pipeline.Drawing;
using KingRenderingSharp.Graph.Pipeline.Shadows;
using KingRenderingSharp.Content.Rendering.Cameras;
using KingRenderingSharp.Content.Rendering.Lighting;

namespace KingRenderingSharp.Graph.Pipeline;

/// <summary>Everything needed to render one frame.</summary>
public sealed record FrameRequest(
    Camera Camera, LightingContext Lighting, IFrameEnvironment Environment, IReadOnlyList<ActorRenderInput> Actors,
    float AoRadius, float ShadowBias,
    (int ActorIndex, int ShapeIndex)? Highlight = null,
    IReadOnlyList<InstanceBatch>? Instances = null,
    ShadowFocus? ShadowFocus = null,
    IReadOnlyList<ShadowFocus>? ShadowCascades = null,
    IReadOnlyList<float>? ShadowSliceEnds = null);
