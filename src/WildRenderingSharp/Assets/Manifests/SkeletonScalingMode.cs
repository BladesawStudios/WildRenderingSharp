
namespace WildRenderingSharp.Assets.Manifests;

/// <summary>
/// How bone scale is propagated down the hierarchy.
/// See BFRES 9.0 FSKA documentation:
/// https://wiki.wexosmk.xyz/index.php/BFRES_(File_Format)/Switch#FSKA
/// </summary>
public enum SkeletonScalingMode
{
    // Bone scale is never applied at all.
    None = 0,
    // Scale multiplies after the parent multiply, and cascades to children
    // like any other part of the transform.
    Standard = 1,
    // Standard, plus segment scale compensate for any bone flagged with it.
    Maya = 2,
    // Unused in the retail game, treated the same as Maya.
    // (the dispatch table at 0x71041da970 runs out at Maya)
    Softimage = 3,
}
