using System.Numerics;
using KingRenderingSharp.Graph.Pipeline.Passes;
using KingRenderingSharp.Graph.Pipeline.Shadows;

namespace KingRenderingSharp.Graph.Pipeline.Frame;

/// <summary>What a frame's shadow stage hands the screen-space lighting: the light's matrices, the region they cover, and the cascades when there are any.</summary>
internal readonly record struct ShadowResult(
    ShadowPass.LightMatrices Light, Vector3 BoundsLo, Vector3 BoundsHi, ScreenSpaceShadowAndAoPass.CascadeParams? Cascades = null);
