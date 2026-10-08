using WildRenderingSharp.Assets;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>Applies the final colour grade to the tonemapped image.</summary>
public sealed class ColorCorrectionStage(FrameServices services) : IFrameStage, IDisposable
{
    readonly ColorCorrectionPass _colorCorrection = new(services.Gl);

    public void Run(FrameContext frame)
    {
        var grade = frame.Request.ColorCorrection ?? ColorCorrectionPostFx.Default;
        if (_colorCorrection.Run(services.Resources, frame.Targets, frame.Targets.Ldr, grade))
            GLDiagnostics.CheckPass(services.Gl, "color correction");
    }

    public void Dispose() => _colorCorrection.Dispose();
}
