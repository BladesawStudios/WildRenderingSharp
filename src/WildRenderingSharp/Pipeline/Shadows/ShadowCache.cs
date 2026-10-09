using System.Numerics;
using WildRenderingSharp.Pipeline.Targets;

namespace WildRenderingSharp.Pipeline.Shadows;

/// <summary>What the last shadow map was drawn from, so an unchanged scene can reuse it.</summary>
public sealed class ShadowCache
{
    internal Vector3? SunWorld;
    internal Vector4[][]? ModelRowsPerActor;
    internal Matrix4x4[]?[]? BonesPerActor;
    internal ShadowFocus? Focus;
    internal long InstanceSignature;
    internal Vector3 RotatedLo, RotatedHi;
    internal ShadowPass.LightMatrices LightMatrices;
    internal readonly long[] CascadeSignature = new long[RenderTargets.MaxCascades];
    internal readonly ShadowPass.LightMatrices[] CascadeLight = new ShadowPass.LightMatrices[RenderTargets.MaxCascades];
    internal readonly float[] CascadeRadius = new float[RenderTargets.MaxCascades];
}
