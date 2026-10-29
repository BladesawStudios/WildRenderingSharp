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
    internal readonly long[] CascadeDrawnAt = new long[RenderTargets.MaxCascades];
    internal long FrameCount;
    internal readonly System.Numerics.Vector3[] CascadeSun = new System.Numerics.Vector3[RenderTargets.MaxCascades];
    internal readonly (int Sun, int Focus, int Terrain, int Runs)[] CascadeParts = new (int, int, int, int)[RenderTargets.MaxCascades];

    // How many redraws of a cascade were because the sun, the cascade's region, the ground or the casters changed.
    internal readonly long[] CascadeCauses = new long[4];
    internal readonly bool[] CascadeHasActors = new bool[RenderTargets.MaxCascades];
    internal readonly long[] CascadeRedraws = new long[RenderTargets.MaxCascades];
    internal int TargetsId, CascadeSize;
    internal readonly float[] CascadeRadius = new float[RenderTargets.MaxCascades];

    // The view depth a cascade fitted to a slice of the view ends at; zero for one that is a region kept between frames.
    internal readonly float[] CascadeSliceEnd = new float[RenderTargets.MaxCascades];
}
