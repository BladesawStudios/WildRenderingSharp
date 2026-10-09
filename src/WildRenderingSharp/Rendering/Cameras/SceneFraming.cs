
namespace WildRenderingSharp.Rendering.Cameras;

/// <summary>Near/far and world-scale constants tuned to a specific model's size.</summary>
internal readonly record struct SceneFraming(float Near, float Far, float AoRadius, float ShadowBias);
