using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Profiles.Totk.Deferred.Resolve;
using WildRenderingSharp.Profiles.Totk.Terrain;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>Draws the opaque shapes, then the host's terrain, into the G-buffer.</summary>
public sealed class GBufferStage(StageServices services, DeferredScene scene, TerrainRenderer terrain) : IFrameStage
{
    readonly GBufferPass _gbuffer = new(services.Gl);

    public void Run(FrameContext frame)
    {
        var gl = services.Gl;

        ClipOrigin.Game(gl, true);
        _gbuffer.Run(services.Resources, frame.Targets, frame.OpaqueGroups, services.Drawer);
        ClipOrigin.Game(gl, false);
        GLDiagnostics.CheckPass(gl, "G-buffer pass");
        frame.GBufferCounts = services.Drawer.TakeCounts();

        frame.TerrainDrawn = false;
        if (frame.TotkEnvironment().Terrain is { } host && terrain.Available)
        {
            frame.TerrainDrawn = true;
            scene.EnsurePass(DeferredScene.DefaultPass);
            terrain.DrawGBuffer(host, frame.Targets, frame.Camera, frame.GameOrigin ? frame.Cam : frame.FlippedCam);
        }

        // Every later pass reads the unflipped projection.
        services.Resources.BindCamera(FrameUniformKeys.SceneCamera);
    }
}
