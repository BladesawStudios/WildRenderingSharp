using WildRenderingSharp.Profiles.Totk.Terrain;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Pipeline;

/// <summary>Everything needed to render one frame.</summary>
/// <param name="Highlight">An actor and shape to overlay with a translucent highlight, or null.</param>
/// <param name="Instances">Batches of placements drawn instanced alongside <paramref name="Actors"/>.</param>
/// <param name="Terrain">A host whose terrain is shaded with the game's terrain programs.</param>
/// <param name="ShadowCascades">
/// Nested shadow regions, finest first, each drawn into its own layer (at most
/// <see cref="RenderTargets.MaxCascades"/>). A cascade is redrawn only when its region, the sun or
/// its casters change. Takes the place of <paramref name="ShadowFocus"/> when given.
/// </param>
/// <param name="ShadowFocus">The region a single shadow map covers, instead of every actor's bounds.</param>
public sealed record FrameRequest(
    Camera Camera, LightingContext Lighting, EnvPalette Palette, IReadOnlyList<ActorRenderInput> Actors,
    float AoRadius, float ShadowBias,
    (int ActorIndex, int ShapeIndex)? Highlight = null,
    SkyPostFx? SkyPostFx = null, CloudPostFx? CloudPostFx = null, SkyBinLut? SkyBin = null,
    ColorCorrectionPostFx? ColorCorrection = null,
    IReadOnlyList<InstanceBatch>? Instances = null,
    ShadowFocus? ShadowFocus = null,
    IReadOnlyList<ShadowFocus>? ShadowCascades = null,
    ITerrainHost? Terrain = null);
