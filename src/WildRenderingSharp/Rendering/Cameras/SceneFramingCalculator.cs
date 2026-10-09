using System.Numerics;

namespace WildRenderingSharp.Rendering.Cameras;

/// <summary>Derives the near and far planes, AO radius and shadow bias from a model's bounding radius, because model scales differ by a factor of hundreds.</summary>
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
