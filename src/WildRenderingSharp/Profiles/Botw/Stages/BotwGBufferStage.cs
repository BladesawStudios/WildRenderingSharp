using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Gpu;
using WildRenderingSharp.Pipeline.Passes;

namespace WildRenderingSharp.Profiles.Botw.Stages;

/// <summary>Draws the opaque shapes into the G-buffer.</summary>
public sealed class BotwGBufferStage(StageServices services) : IFrameStage
{
    readonly GBufferPass _gbuffer = new(services.Gl);

    public void Run(FrameContext frame)
    {
        ClipOrigin.Game(services.Gl, true);
        _gbuffer.Run(services.Resources, frame.Targets, frame.OpaqueGroups, services.Drawer);
        ClipOrigin.Game(services.Gl, false);
        GLDiagnostics.CheckPass(services.Gl, "G-buffer pass");
        frame.GBufferCounts = services.Drawer.TakeCounts();
        services.Resources.BindCamera(FrameUniformKeys.SceneCamera);
    }
}
