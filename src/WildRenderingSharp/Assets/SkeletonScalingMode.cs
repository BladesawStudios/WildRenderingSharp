
namespace WildRenderingSharp.Assets;

/// <summary>
/// How a skeleton's bone scales propagate down the hierarchy - <c>(FSKL.flags &gt;&gt; 8) &amp; 3</c>, exactly the value
/// <c>nn::g3d2::SkeletonObj::CalculateWorldMtx</c> (Ghidra 0x710008246c) switches its three <c>CalculateWorldImpl</c>
/// specialisations on.
/// </summary>
public enum SkeletonScalingMode
{
    None = 0,
    Standard = 1,
    Maya = 2,
    Softimage = 3,
}
