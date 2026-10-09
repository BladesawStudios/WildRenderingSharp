using WildRenderingSharp.Pipeline.Targets;

namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>A one-shot request to measure the exposure a scene needs, answered on the next frame.</summary>
public sealed class ExposureProbe
{
    bool _requested;

    public ExposureMeter.Result? Last { get; private set; }

    public void Request() => _requested = true;

    public void MeasureIfRequested(RenderTargets targets, float currentExposure)
    {
        if (!_requested)
            return;
        _requested = false;

        Last = ExposureMeter.Measure(targets, targets.Final);
        if (Last is { } m)
            Console.WriteLine($"[ExposureMeter] geometric-mean luminance {m.GeometricMeanLuminance:G4}, " +
                $"max {m.MaxLuminance:G4}, {m.SampleCount} samples -> suggested Exposure {m.SuggestedExposure:G4} " +
                $"(currently {currentExposure:G4})");
        else
            Console.WriteLine("[ExposureMeter] nothing lit enough to measure.");
    }
}
