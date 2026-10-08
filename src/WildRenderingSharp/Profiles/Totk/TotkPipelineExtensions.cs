using WildRenderingSharp.Pipeline;

namespace WildRenderingSharp.Profiles.Totk;

public static class TotkPipelineExtensions
{
    /// <summary>The pipeline's frame graph as TotK's, for the parts of it only TotK has.</summary>
    public static TotkFrameGraph TotkGraph(this DeferredPipeline pipeline) =>
        pipeline.Graph as TotkFrameGraph ?? throw new InvalidOperationException("The pipeline is not running TotK's frame graph.");
}
