
namespace WildRenderingSharp.Rendering.Cameras;

/// <summary>Near/far and world-scale constants tuned to a specific model's size.</summary>
public readonly record struct SceneFraming(float Near, float Far, float AoRadius, float ShadowBias);
