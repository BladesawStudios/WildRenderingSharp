using WildRenderingSharp.Graphics;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Pipeline;

/// <summary>Everything needed to render one frame.</summary>
/// <param name="Environment">The game's environment data for this frame (palette, sky, grade); a profile's stages read its concrete type.</param>
/// <param name="Highlight">An actor and shape to overlay with a translucent highlight, or null.</param>
/// <param name="Instances">Batches of placements drawn instanced alongside <paramref name="Actors"/>.</param>
/// <param name="ShadowCascades">
/// Nested shadow regions, finest first, each drawn into its own layer (at most
/// <see cref="RenderTargets.MaxCascades"/>). A cascade is redrawn only when its region, the sun or
/// its casters change. Takes the place of <paramref name="ShadowFocus"/> when given.
/// </param>
/// <param name="ShadowFocus">The region a single shadow map covers, instead of every actor's bounds.</param>
public sealed record FrameRequest(
    Camera Camera, LightingContext Lighting, IFrameEnvironment Environment, IReadOnlyList<ActorRenderInput> Actors,
    float AoRadius, float ShadowBias,
    (int ActorIndex, int ShapeIndex)? Highlight = null,
    IReadOnlyList<InstanceBatch>? Instances = null,
    ShadowFocus? ShadowFocus = null,
    IReadOnlyList<ShadowFocus>? ShadowCascades = null);
