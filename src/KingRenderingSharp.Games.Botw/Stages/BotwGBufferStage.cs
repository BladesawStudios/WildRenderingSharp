using KingRenderingSharp.Gpu;
using KingRenderingSharp.Content.Graphics.Ubos;
using KingRenderingSharp.Graph.Pipeline.Frame;
using KingRenderingSharp.Graph.Pipeline.Passes;

namespace KingRenderingSharp.Games.Botw.Stages;

/// <summary>Draws the opaque shapes into the G-buffer.</summary>
internal sealed class BotwGBufferStage(StageServices services) : IFrameStage
{
    readonly GBufferPass _gbuffer = new(services.Gl);

    public void Run(FrameContext frame)
    {
        ClipOrigin.Game(services.Gl, true);
        _gbuffer.Run(services.Resources, frame.Targets, frame.Setup.OpaqueGroups, services.Drawer);
        ClipOrigin.Game(services.Gl, false);
        GLDiagnostics.CheckPass(services.Gl, "G-buffer pass");
        frame.Stats.GBuffer = services.Drawer.TakeCounts();
        services.Resources.BindCamera(FrameUniformKeys.SceneCamera);
    }
}
