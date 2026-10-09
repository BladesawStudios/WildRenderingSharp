using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Botw.Stages;

namespace WildRenderingSharp.Profiles.Botw;

/// <summary>BotW's frame: a G-buffer, a lighting resolve and a tonemap.</summary>
public sealed class BotwFrameGraph : IFrameGraph
{
    readonly List<IFrameStage> _stages;
    readonly List<IDisposable> _owned = [];

    public BotwFrameGraph(StageServices services)
    {
        var passes = Own(new BotwPasses(services));
        var lighting = Own(new BotwLightingStage(services, passes));
        _stages =
        [
            new FrameSetupStage(services),
            Own(new BotwFrameConstantsStage(services)),
            new BotwGBufferStage(services),
            lighting,
            Own(new BotwResolveStage(services, passes, lighting)),
            Own(new BotwTonemapStage(services)),
        ];
    }

    T Own<T>(T item) where T : IDisposable
    {
        _owned.Add(item);
        return item;
    }

    public void SetScene(IReadOnlyList<LoadedModel> models)
    {
    }

    public void PrepareEnvironment(IFrameEnvironment environment)
    {
    }

    public void Run(FrameContext frame)
    {
        foreach (var stage in _stages)
            stage.Run(frame);
    }

    public void Dispose()
    {
        foreach (var item in Enumerable.Reverse(_owned))
            item.Dispose();
        _owned.Clear();
    }
}
