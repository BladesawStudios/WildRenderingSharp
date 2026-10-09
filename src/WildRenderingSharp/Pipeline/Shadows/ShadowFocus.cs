using System.Numerics;

namespace WildRenderingSharp.Pipeline.Shadows;

/// <summary>A sphere the shadow map is fitted to.</summary>
public readonly record struct ShadowFocus(Vector3 Center, float Radius)
{
    // The corners of the axis-aligned box around the sphere.
    public Vector3 Min => Center - new Vector3(Radius);

    public Vector3 Max => Center + new Vector3(Radius);
}
