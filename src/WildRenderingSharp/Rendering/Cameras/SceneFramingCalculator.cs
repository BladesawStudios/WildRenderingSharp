using System.Numerics;

namespace WildRenderingSharp.Rendering.Cameras;

/// <summary>
/// TotK model scales vary enormously - the Master Sword's bounding sphere has radius 0.74, the Light Dragon's is 694, a factor of
/// 940 - so every world-unit constant tuned against the sword (near/far planes, AO radius and shadow bias)
/// is re-derived per model as a FRACTION of its own radius.
/// </summary>
internal static class SceneFramingCalculator
{
    const float SwordRadius = 0.7386f;
    const float AoRadiusFraction = 0.035f / SwordRadius;
    const float ShadowBiasFraction = 0.0015f / SwordRadius;
    const float NearFraction = 0.05f / SwordRadius;
    const float FarFraction = 10.0f / SwordRadius;

    public static readonly Vector3 DefaultViewDirection = Vector3.Normalize(new Vector3(0.2571f, 0f, -1.1996f));

    public static SceneFraming ForModelRadius(float radius) => new(
        Near: radius * NearFraction,
        Far: radius * FarFraction,
        AoRadius: radius * AoRadiusFraction,
        ShadowBias: radius * ShadowBiasFraction);
}
