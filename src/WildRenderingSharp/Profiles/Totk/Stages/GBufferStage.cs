using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Profiles.Totk.Deferred.Resolve;
using WildRenderingSharp.Profiles.Totk.Terrain;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>Draws the opaque shapes, then the host's terrain, into the G-buffer.</summary>
internal sealed class GBufferStage(StageServices services, DeferredScene scene, TerrainRenderer terrain) : IFrameStage
{
    readonly GBufferPass _gbuffer = new(services.Gl);

    public void Run(FrameContext frame)
    {
        var gl = services.Gl;

        ClipOrigin.Game(gl, true);
        _gbuffer.Run(services.Resources, frame.Targets, frame.Setup.OpaqueGroups, services.Drawer);
        ClipOrigin.Game(gl, false);
        GLDiagnostics.CheckPass(gl, "G-buffer pass");
        frame.Stats.GBuffer = services.Drawer.TakeCounts();

        terrain.BeginFrame();
        if (frame.TotkEnvironment().Terrain is { } host && terrain.Available)
        {
            scene.EnsurePass(DeferredScene.DefaultPass);
            terrain.DrawGBuffer(host, frame.Targets, frame.Camera, frame.Setup.GameOrigin ? frame.Setup.Cam : frame.Setup.FlippedCam);
        }

        // Every later pass reads the unflipped projection.
        services.Resources.BindCamera(FrameUniformKeys.SceneCamera);
    }
}
