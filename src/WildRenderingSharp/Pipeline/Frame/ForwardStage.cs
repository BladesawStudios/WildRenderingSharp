using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Passes;

namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>Draws blended materials over the resolved scene.</summary>
internal sealed class ForwardStage(StageServices services, ForwardPass forward) : IFrameStage
{
    public void Run(FrameContext frame)
    {
        forward.Run(services.Resources, frame.Targets, frame.Setup.Groups, services.Drawer);
        GLDiagnostics.CheckPass(services.Gl, "forward pass");
        services.Resources.BindCamera(FrameUniformKeys.SceneCamera);
    }
}
