using KingRenderingSharp.Gpu;
using KingRenderingSharp.Content.Graphics.Ubos;
using KingRenderingSharp.Graph.Pipeline.Frame;
using KingRenderingSharp.Graph.Pipeline.Passes;
using KingRenderingSharp.Games.Totk.Atmosphere;
using KingRenderingSharp.Games.Totk.Deferred.Resolve;
using KingRenderingSharp.Games.Totk.Ubos;

namespace KingRenderingSharp.Games.Totk.Stages;

/// <summary>Exposure, bloom and the game's HDR compose, producing the display-range image.</summary>
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

        var exposed = _tonemap.RunExposure(services.Resources, targets, lighting.Exposure);
        GLDiagnostics.CheckPass(services.Gl, "exposure");

        // The palette owns bloom's shape and authored strength; the viewer's intensity scales it.
        float bloomIntensity = palette.BloomEnable ? palette.BloomIntensity * lighting.BloomIntensity : 0f;
        _bloom.Run(services.Resources, targets, exposed, palette.BloomThreshold, palette.BloomClampedLuminance, bloomIntensity);
        GLDiagnostics.CheckPass(services.Gl, "bloom");

        _tonemap.RunHdrComposite(services.Resources, targets, _hdrComposeProgram, exposed, targets.Bloom,
            HdrComposeParams(frame.TotkEnvironment().HdrCompose));
    }

    // cParam is the only field the shader reads: the saturation at the dark end, and how far it rises to the bright end's.
    internal static Ubo HdrComposeParams(HdrComposeParam param)
    {
        var block = new UboWriter(TotkBlocks.HdrComposeParams);
        block.Set(0, param.SaturationBottom, param.SaturationTop - param.SaturationBottom, 0f, 0f);
        return block.ToUbo("hdr_compose_params");
    }

    public void Dispose()
    {
        _tonemap.Dispose();
        _bloom.Dispose();
    }
}
