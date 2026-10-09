using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Atmosphere;

namespace WildRenderingSharp.Profiles.Totk;

/// <summary>The TotK environment of a frame.</summary>
static class TotkFrameContextExtensions
{
    public static TotkEnvironment TotkEnvironment(this FrameContext frame) =>
        frame.Environment as TotkEnvironment
        ?? throw new InvalidOperationException($"TotK stages need a {nameof(Atmosphere.TotkEnvironment)}, not {frame.Environment.GetType().Name}.");
}
