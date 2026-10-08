using WildRenderingSharp.Assets;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Deferred;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>Stamps each pixel with the deferred pass that lights it, in the unflipped view the resolve reads.</summary>
public sealed class PassIdMaskStage(FrameServices services, PassIdMaskPass passIdMask, DeferredScene scene) : IFrameStage
{
    public void Run(FrameContext frame)
    {
        passIdMask.Run(services.Resources, frame.Targets, frame.Groups, scene.PassNames, frame.MaskViewProj,
            frame.Camera.NearPlane, frame.Camera.FarPlane);
        GLDiagnostics.CheckPass(services.Gl, "pass-ID mask");
    }
}
