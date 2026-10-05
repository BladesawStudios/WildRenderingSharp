using System.Numerics;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// One placed actor's shapes plus the per-actor GPU skinning resources every shape-drawing pass
/// needs bound before drawing them - the real compiled game shaders read bone transforms from
/// shared UBO binding points (<c>_Mtx</c>/binding 2, <c>ShpMtx</c>/binding 4) with no per-draw
/// instance addressing at all, so multi-actor rendering works by REBINDING a different actor's
/// own buffer immediately before that actor's own draw calls - exactly what a real engine does
/// between per-object draw calls - rather than trying to combine every actor into one buffer.
/// See <see cref="DeferredPipeline"/>'s remarks for the full reasoning.
/// </summary>
public readonly record struct ActorDrawGroup(byte[] BonesBytes, byte[] ShpMtxBytes, Vector4[] ModelMatrixRows, IReadOnlyList<LoadedShape> Shapes)
{
    /// <summary>Rebinds THIS actor's own bone-palette/ShpMtx buffers to their shared binding points - call immediately before drawing this group's shapes in any pass, and again for the next group before its own shapes.</summary>
    public void BindUbos(GLResourceCache resources)
    {
        resources.Ubo("bones", BonesBytes, bindingIndex: 2);
        resources.Ubo("shpmtx", ShpMtxBytes, bindingIndex: 4);
    }
}
