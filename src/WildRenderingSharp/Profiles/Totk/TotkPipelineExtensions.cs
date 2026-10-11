using WildRenderingSharp.Pipeline;

namespace WildRenderingSharp.Profiles.Totk;

/// <summary>The TotK frame graph of a pipeline.</summary>
internal static class TotkPipelineExtensions
{
    // The pipeline's frame graph as TotK's, for the parts of it only TotK has.
    public static TotkFrameGraph TotkGraph(this DeferredPipeline pipeline) =>
        pipeline.Graph as TotkFrameGraph ?? throw new InvalidOperationException("The pipeline is not running TotK's frame graph.");
}
