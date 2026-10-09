using WildRenderingSharp.Assets;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Gpu;
using WildRenderingSharp.Profiles.Totk.Deferred;
using WildRenderingSharp.Profiles.Totk.Deferred.PassIds;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>Stamps each pixel with the deferred pass that lights it, in the unflipped view the resolve reads.</summary>
public sealed class PassIdMaskStage(StageServices services, PassIdStamper stamper) : IFrameStage
{
    public void Run(FrameContext frame)
    {
        stamper.Run(services.Resources, frame);
        GLDiagnostics.CheckPass(services.Gl, "pass-ID mask");
    }
}
