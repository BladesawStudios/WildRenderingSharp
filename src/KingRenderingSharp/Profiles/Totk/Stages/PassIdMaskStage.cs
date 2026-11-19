using KingRenderingSharp.Gpu;
using KingRenderingSharp.Pipeline.Frame;
using KingRenderingSharp.Profiles.Totk.Deferred.PassIds;

namespace KingRenderingSharp.Profiles.Totk.Stages;

/// <summary>Stamps each pixel with the deferred pass that lights it, in the unflipped view the resolve reads.</summary>
internal sealed class PassIdMaskStage(StageServices services, PassIdStamper stamper) : IFrameStage
{
    public void Run(FrameContext frame)
    {
        stamper.Run(services.Resources, frame);
        GLDiagnostics.CheckPass(services.Gl, "pass-ID mask");
    }
}
