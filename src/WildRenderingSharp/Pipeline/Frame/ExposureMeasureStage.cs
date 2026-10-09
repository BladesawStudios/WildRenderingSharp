namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>Answers a pending exposure measurement while the image is still pre-exposure HDR.</summary>
public sealed class ExposureMeasureStage(StageServices services) : IFrameStage
{
    public void Run(FrameContext frame) =>
        services.Exposure.MeasureIfRequested(frame.Targets, frame.Lighting.Exposure);
}
