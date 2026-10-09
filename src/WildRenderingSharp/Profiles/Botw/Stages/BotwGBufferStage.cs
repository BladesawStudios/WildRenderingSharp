using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Frame;

namespace WildRenderingSharp.Profiles.Botw.Stages;

/// <summary>Draws the opaque shapes into the G-buffer.</summary>
public sealed class BotwGBufferStage(FrameServices services) : IFrameStage
{
    readonly GBufferPass _gbuffer = new(services.Gl);

    public void Run(FrameContext frame)
    {
        ClipOrigin.Game(services.Gl, true);
        _gbuffer.Run(services.Resources, frame.Targets, frame.OpaqueGroups, services.Programs);
        ClipOrigin.Game(services.Gl, false);
        GLDiagnostics.CheckPass(services.Gl, "G-buffer pass");
        frame.GBufferCounts = ShapeDrawing.TakeCounts();
        services.Resources.BindCamera(FrameUniformKeys.SceneCamera);
    }
}
