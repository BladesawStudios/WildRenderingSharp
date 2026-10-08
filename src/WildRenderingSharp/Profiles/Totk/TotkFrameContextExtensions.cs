using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Atmosphere;

namespace WildRenderingSharp.Profiles.Totk;

static class TotkFrameContextExtensions
{
    /// <summary>The frame's environment as TotK's, which is what every TotK stage reads.</summary>
    public static TotkEnvironment TotkEnvironment(this FrameContext frame) =>
        frame.Environment as TotkEnvironment
        ?? throw new InvalidOperationException($"TotK stages need a {nameof(Atmosphere.TotkEnvironment)}, not {frame.Environment.GetType().Name}.");
}
