using KingRenderingSharp.Pipeline.Frame;
using KingRenderingSharp.Pipeline.Passes;
using KingRenderingSharp.Profiles.Totk.Deferred.PreShading;

namespace KingRenderingSharp.Profiles.Totk.Stages;

/// <summary>Hands the sky inputs the pre-shading bound to the forward programs, whose own fog reads the same environment cube, inscatter table and volume mask.</summary>
internal sealed class ForwardSkyStage(ForwardNeutralInputs inputs, GamePreShading preShading) : IFrameStage
{
    public void Run(FrameContext frame) => inputs.Sky = preShading.Sky;
}
