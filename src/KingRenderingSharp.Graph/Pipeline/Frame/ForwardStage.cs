using KingRenderingSharp.Gpu;
using KingRenderingSharp.Content.Graphics.Ubos;
using KingRenderingSharp.Graph.Pipeline.Passes;

namespace KingRenderingSharp.Graph.Pipeline.Frame;

/// <summary>Draws blended materials over the resolved scene.</summary>
internal sealed class ForwardStage(StageServices services, ForwardPass forward) : IFrameStage
{
    public void Run(FrameContext frame)
    {
        forward.Run(services.Resources, frame.Targets, frame.Setup.Groups, services.Drawer, frame.Lighting.Exposure / MathF.Max(1e-4f, frame.Lighting.EmissionScale));
        GLDiagnostics.CheckPass(services.Gl, "forward pass");
        services.Resources.BindCamera(FrameUniformKeys.SceneCamera);
    }
}
