using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Profiles.Totk.Deferred.Resolve;
using WildRenderingSharp.Profiles.Totk.Terrain;

namespace WildRenderingSharp.Profiles.Totk.Deferred.PassIds;

/// <summary>Fills the pass-ID mask: first from each shape's named pass, then from the material IDs the G-buffer programs wrote, then with the entrance's pass over a shrine's doorway.</summary>
internal sealed class PassIdStamper(GL gl, ShapeDrawer drawer, DeferredScene scene, TerrainRenderer terrain, EntranceInterior interior) : IDisposable
{
    readonly PassIdMaskPass _byShape = new(gl, drawer);
    readonly MaterialIdPass _byMaterialId = new(gl);

    // The pass that lights every pixel the mask leaves at zero (a host's terrain), or -1. Its own shapes are not stamped.
    public int ClaimPass(FrameContext frame) =>
        frame.TotkEnvironment().Terrain is not null ? scene.PassIndex(DeferredScene.DefaultPass) : -1;

    public void Run(GLResourceCache resources, FrameContext frame)
    {
        _byShape.Run(resources, frame.Targets, frame.Setup.Groups, scene.PassNames, scene.PassOf, frame.Setup.Cam.ViewProj,
            frame.Camera.NearPlane, frame.Camera.FarPlane, ClaimPass(frame));
        // The host's draws leave the layers they do not write as the shapes behind them left them, so the IDs under what they covered are stale.
        _byMaterialId.Run(resources, frame.Targets, scene.MaterialIdPasses(), terrain.GBufferDrawn, frame.Camera.NearPlane, frame.Camera.FarPlane);
        interior.Stamp(resources, scene.PassIndex(LoadedShapeExtensions.EntrancePass));
    }

    public void Dispose()
    {
        _byShape.Dispose();
        _byMaterialId.Dispose();
    }
}
