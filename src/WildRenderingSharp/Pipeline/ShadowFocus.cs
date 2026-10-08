using System.Numerics;

namespace WildRenderingSharp.Pipeline;

/// <summary>A sphere the shadow map is fitted to.</summary>
public readonly record struct ShadowFocus(Vector3 Center, float Radius);
