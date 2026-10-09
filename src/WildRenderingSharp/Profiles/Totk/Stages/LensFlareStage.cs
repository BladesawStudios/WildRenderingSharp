using System.Numerics;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Sky.LensFlare;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>Adds the game's lens flare to the HDR image, before exposure so it lives in the same linear space as the scene.</summary>
public sealed class LensFlareStage(StageServices services) : IFrameStage, IDisposable
{
    readonly LensFlarePass _lensFlare = new(services.Gl, services.Programs);

    public void Run(FrameContext frame)
    {
        var lighting = frame.Lighting;
        var settings = frame.TotkEnvironment().Settings;
        if (!settings.UseLensFlare)
            return;

        _lensFlare.Run(services.Resources, frame.Targets, frame.Targets.Final, new LensFlarePass.Params(
            Threshold: settings.LensFlareThreshold,
            GhostSpacing: settings.LensFlareGhostSpacing,
            HaloTint: Vector3.One,
            HaloRadius: settings.LensFlareHaloRadius,
            Intensity: Vector3.One * settings.LensFlareIntensity,
            Exposure: lighting.Exposure,
            SkyOnly: settings.LensFlareSkyOnly));
        GLDiagnostics.CheckPass(services.Gl, "lens flare");
    }

    public void Dispose() => _lensFlare.Dispose();
}
