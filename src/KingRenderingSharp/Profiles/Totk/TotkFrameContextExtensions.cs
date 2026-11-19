using KingRenderingSharp.Pipeline.Frame;
using KingRenderingSharp.Profiles.Totk.Atmosphere;

namespace KingRenderingSharp.Profiles.Totk;

/// <summary>The TotK environment of a frame.</summary>
static class TotkFrameContextExtensions
{
    public static TotkEnvironment TotkEnvironment(this FrameContext frame) =>
        frame.Environment as TotkEnvironment
        ?? throw new InvalidOperationException($"TotK stages need a {nameof(Atmosphere.TotkEnvironment)}, not {frame.Environment.GetType().Name}.");
}
