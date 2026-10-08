using WildRenderingSharp.Assets;
using System.Numerics;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Sky;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>Adds the game's lens flare to the HDR image, before exposure so it lives in the same linear space as the scene.</summary>
public sealed class LensFlareStage(FrameServices services) : IFrameStage, IDisposable
{
    readonly LensFlarePass _lensFlare = new(services.Gl, services.Programs);

    public void Run(FrameContext frame)
    {
        var lighting = frame.Lighting;
        if (!lighting.UseLensFlare)
            return;

        _lensFlare.Run(services.Resources, frame.Targets, frame.Targets.Final, new LensFlarePass.Params(
            Threshold: lighting.LensFlareThreshold,
            GhostSpacing: lighting.LensFlareGhostSpacing,
            HaloTint: Vector3.One,
            HaloRadius: lighting.LensFlareHaloRadius,
            Intensity: Vector3.One * lighting.LensFlareIntensity,
            Exposure: lighting.Exposure,
            SkyOnly: lighting.LensFlareSkyOnly));
        GLDiagnostics.CheckPass(services.Gl, "lens flare");
    }

    public void Dispose() => _lensFlare.Dispose();
}
