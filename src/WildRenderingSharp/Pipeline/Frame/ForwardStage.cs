using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>Draws blended materials over the resolved scene.</summary>
public sealed class ForwardStage(StageServices services, ForwardPass forward) : IFrameStage
{
    public void Run(FrameContext frame)
    {
        forward.Run(services.Resources, frame.Targets, frame.Groups, services.Programs);
        GLDiagnostics.CheckPass(services.Gl, "forward pass");
        services.Resources.BindCamera(FrameUniformKeys.SceneCamera);
    }
}
