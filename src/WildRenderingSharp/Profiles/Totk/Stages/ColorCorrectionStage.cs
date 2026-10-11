using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.PostProcess;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>Applies the final colour grade to the tonemapped image.</summary>
internal sealed class ColorCorrectionStage(StageServices services) : IFrameStage, IDisposable
{
    readonly ColorCorrectionPass _colorCorrection = new(services.Gl);

    public void Run(FrameContext frame)
    {
        var grade = frame.TotkEnvironment().ColorCorrection;
        if (_colorCorrection.Run(services.Resources, frame.Targets, frame.Targets.Ldr, grade))
            GLDiagnostics.CheckPass(services.Gl, "color correction");
    }

    public void Dispose() => _colorCorrection.Dispose();
}
