using KingRenderingSharp.Graph.Pipeline.Frame;
using KingRenderingSharp.Games.Totk.Atmosphere;

namespace KingRenderingSharp.Games.Totk;

/// <summary>The TotK environment of a frame.</summary>
static class TotkFrameContextExtensions
{
    public static TotkEnvironment TotkEnvironment(this FrameContext frame) =>
        frame.Environment as TotkEnvironment
        ?? throw new InvalidOperationException($"TotK stages need a {nameof(Atmosphere.TotkEnvironment)}, not {frame.Environment.GetType().Name}.");
}
