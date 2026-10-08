
namespace WildRenderingSharp.Assets;

/// <summary>How a skeleton's bone scales propagate down the hierarchy - <c>(FSKL.flags &gt;&gt; 8) &amp; 3</c>, exactly the value <c>nn::g3d2::SkeletonObj::CalculateWorldMtx</c> (Ghidra 0x710008246c) switches its three <c>CalculateWorldImpl</c> specialisations on.</summary>
public enum SkeletonScalingMode
{
    /// <summary><c>CalculateWorldImpl&lt;CalculateWorldNoScale&gt;</c> (0x7100080b40) - bone scale is never applied at all.</summary>
    None = 0,
    /// <summary><c>CalculateWorldImpl&lt;CalculateWorldStd&gt;</c> (0x7100080d00) - scale multiplies world rows 0/1/2 after the parent multiply, and cascades to children like any other part of the transform.</summary>
    Standard = 1,
    /// <summary><c>CalculateWorldImpl&lt;CalculateWorldMaya&gt;</c> (0x7100080f2c) - Standard, plus segment scale compensate for any bone flagged with it.</summary>
    Maya = 2,
    /// <summary>No dedicated specialisation exists in the shipped binary (the dispatch table at 0x71041da970 runs out at Maya); treated as <see cref="Maya"/>.</summary>
    Softimage = 3,
}
