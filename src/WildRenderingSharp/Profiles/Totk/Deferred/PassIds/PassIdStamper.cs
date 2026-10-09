using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Gpu;
using WildRenderingSharp.Profiles.Totk.Deferred.Resolve;

namespace WildRenderingSharp.Profiles.Totk.Deferred.PassIds;

/// <summary>Fills the pass-ID mask: first from each shape's named pass, then from the material IDs the G-buffer programs wrote.</summary>
public sealed class PassIdStamper(GL gl, ShapeDrawer drawer, DeferredScene scene) : IDisposable
{
    readonly PassIdMaskPass _byShape = new(gl, drawer);
    readonly MaterialIdPass _byMaterialId = new(gl);

    /// <summary>The pass that lights every pixel the mask leaves at zero (a host's terrain), or -1. Its own shapes are not stamped.</summary>
    public int ClaimPass(FrameContext frame) =>
        frame.TotkEnvironment().Terrain is not null ? scene.PassIndex(DeferredScene.DefaultPass) : -1;

    public void Run(GLResourceCache resources, FrameContext frame)
    {
        _byShape.Run(resources, frame.Targets, frame.Groups, scene.PassNames, frame.MaskViewProj,
            frame.Camera.NearPlane, frame.Camera.FarPlane, ClaimPass(frame));
        _byMaterialId.Run(resources, frame.Targets, scene.MaterialIdPasses());
    }

    public void Dispose()
    {
        _byShape.Dispose();
        _byMaterialId.Dispose();
    }
}
