using WildRenderingSharp.Assets;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Deferred;
using WildRenderingSharp.Profiles.Totk.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>Exposure, highlight compression, bloom and the game's HDR compose, producing the display-range image.</summary>
public sealed class TonemapStage(FrameServices services) : IFrameStage, IDisposable
{
    readonly TonemapPass _tonemap = new(services.Gl);
    readonly BloomPass _bloom = new(services.Gl);
    readonly uint _hdrComposeProgram = services.Programs.Load("agl_hdr_compose");

    public void Run(FrameContext frame)
    {
        var lighting = frame.Lighting;
        var palette = frame.Palette;
        var targets = frame.Targets;

        frame.HdrCompressed = _tonemap.RunExposureAndCompress(services.Resources, targets, lighting.Exposure);
        GLDiagnostics.CheckPass(services.Gl, "exposure/compress");

        // The palette owns bloom's shape and authored strength; the viewer's intensity scales it.
        float bloomIntensity = palette.BloomEnable ? palette.BloomIntensity * lighting.BloomIntensity : 0f;
        _bloom.Run(services.Resources, targets, frame.HdrCompressed, palette.BloomThreshold, palette.BloomClampedLuminance, bloomIntensity);
        GLDiagnostics.CheckPass(services.Gl, "bloom");

        _tonemap.RunHdrComposite(services.Resources, targets, _hdrComposeProgram, frame.HdrCompressed, targets.Bloom,
            HdrComposeParamsUbo.BuildDefault().ToByteArray());
    }

    public void Dispose()
    {
        _tonemap.Dispose();
        _bloom.Dispose();
    }
}
