using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Profiles.Totk.Deferred;
using WildRenderingSharp.Profiles.Totk.Deferred.Resolve;
using WildRenderingSharp.Profiles.Totk.Terrain;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>Draws the opaque shapes, then the host's terrain, into the G-buffer.</summary>
internal sealed class GBufferStage(StageServices services, DeferredScene scene, TerrainRenderer terrain, GBufferControlInputs control,
    EntranceInterior interior) : IFrameStage
{
    // cTex_HalfNormalizedLinearDepth's binding in the game's G-buffer programs.
    const int HalfDepthUnit = 5;

    readonly GBufferPass _gbuffer = new(services.Gl);

    public void Run(FrameContext frame)
    {
        var gl = services.Gl;

        services.Drawer.Merged.Enabled = frame.TotkEnvironment().Settings.MergedDraws;
        ClipOrigin.Game(gl, true);
        control.Bind();
        // The ice's G-buffer program reads the half-resolution depth to measure how thick it is. Nothing else leaves that unit bound to a depth, so without this it read
        // whichever texture the last pass of the frame before had on it - an image of the scene - and drew that as black blotches. The frame before's depth is what is there to read.
        gl.BindTextureAt(HalfDepthUnit, frame.Targets.LinearDepthHalf.Handle);
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

        // A shrine entrance's hall, through its doorway, over whatever stands where it is seen, the terrain above all; so it runs once everything is in.
        interior.Run(services.Resources, frame.Targets, frame, services.Profile, services.Drawer, control);
        GLDiagnostics.CheckPass(gl, "entrance interior");

        // Every later pass reads the unflipped projection.
        services.Resources.BindCamera(FrameUniformKeys.SceneCamera);
    }
}
