using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Profiles.Totk.Deferred.Resolve;
using WildRenderingSharp.Profiles.Totk.Ubos;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>Exposure, highlight compression, bloom and the game's HDR compose, producing the display-range image.</summary>
internal sealed class TonemapStage(StageServices services) : IFrameStage, IDisposable
{
    readonly TonemapPass _tonemap = new(services.Gl);
    readonly BloomPass _bloom = new(services.Gl);
    readonly uint _hdrComposeProgram = services.Programs.Load("agl_hdr_compose");

    public void Run(FrameContext frame)
    {
        var lighting = frame.Lighting;
        var palette = frame.TotkEnvironment().Palette;
        var targets = frame.Targets;

        frame.HdrCompressed = _tonemap.RunExposureAndCompress(services.Resources, targets, lighting.Exposure);
        GLDiagnostics.CheckPass(services.Gl, "exposure/compress");

        // The palette owns bloom's shape and authored strength; the viewer's intensity scales it.
        float bloomIntensity = palette.BloomEnable ? palette.BloomIntensity * lighting.BloomIntensity : 0f;
        _bloom.Run(services.Resources, targets, frame.HdrCompressed, palette.BloomThreshold, palette.BloomClampedLuminance, bloomIntensity);
        GLDiagnostics.CheckPass(services.Gl, "bloom");

        _tonemap.RunHdrComposite(services.Resources, targets, _hdrComposeProgram, frame.HdrCompressed, targets.Bloom,
            HdrComposeParams());
    }

    // cParam is the only field the shader reads.
    internal static Ubo HdrComposeParams()
    {
        var block = new UboWriter(TotkBlocks.HdrComposeParams);
        block.Set(0, 1f, 0f, 0f, 0f);
        return block.ToUbo("hdr_compose_params");
    }

    public void Dispose()
    {
        _tonemap.Dispose();
        _bloom.Dispose();
    }
}
