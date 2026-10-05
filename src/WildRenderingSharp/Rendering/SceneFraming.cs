using System.Numerics;

namespace WildRenderingSharp.Rendering;

/// <summary>Near/far, world-scale, and orbit-distance-clamp constants tuned to a specific model's size.</summary>
public readonly record struct SceneFraming(float Near, float Far, float AoRadius, float ShadowBias, float MinDistance, float MaxDistance);

/// <summary>
/// TotK model scales vary enormously - the Master Sword's bounding sphere has radius 0.74, the
/// Light Dragon's is 694, a factor of 940 - so every world-unit constant tuned against the sword
/// (near/far planes, AO radius, shadow bias, and the orbit camera's own dolly-distance clamp) is
/// re-derived per model as a FRACTION of its own radius. The fractions are exactly the sword's own
/// tuned values divided by the sword's radius, so framing the sword reproduces its numbers bit for
/// bit. Mirrors <c>render_deferred_master_sword.py</c>'s <c>SWORD_RADIUS</c>/<c>*_FRAC</c>
/// constants and <c>frame_model</c> - <c>viewer.py</c>'s own <c>self.dist</c> clamp
/// (<c>max(0.4, min(12.0, ...))</c>) is hardcoded to the sword's scale and was never rescaled per
/// model, which is why panning/dollying on anything bigger than the sword reads as "stuck" (the
/// distance can never grow past a value that's tiny relative to the model).
/// </summary>
public static class SceneFramingCalculator
{
    const float SwordRadius = 0.7386f;
    const float AoRadiusFraction = 0.035f / SwordRadius;
    const float ShadowBiasFraction = 0.0015f / SwordRadius;
    const float NearFraction = 0.05f / SwordRadius;
    const float FarFraction = 10.0f / SwordRadius;
    const float MinDistanceFraction = 0.4f / SwordRadius;
    const float MaxDistanceFraction = 12.0f / SwordRadius;

    /// <summary>The sword's original three-quarter showcase view direction, applicable at any model scale.</summary>
    public static readonly Vector3 DefaultViewDirection = Vector3.Normalize(new Vector3(0.2571f, 1.1996f, 0f));

    public static SceneFraming ForModelRadius(float radius) => new(
        Near: radius * NearFraction,
        Far: radius * FarFraction,
        AoRadius: radius * AoRadiusFraction,
        ShadowBias: radius * ShadowBiasFraction,
        MinDistance: radius * MinDistanceFraction,
        MaxDistance: radius * MaxDistanceFraction);
}
