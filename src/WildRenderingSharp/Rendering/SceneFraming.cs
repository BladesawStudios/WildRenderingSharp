
namespace WildRenderingSharp.Rendering;

/// <summary>Near/far, world-scale, and orbit-distance-clamp constants tuned to a specific model's size.</summary>
public readonly record struct SceneFraming(float Near, float Far, float AoRadius, float ShadowBias, float MinDistance, float MaxDistance);
