using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics.Contracts;

namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>A game's ordered frame, with whatever scene and environment state its stages share.</summary>
public interface IFrameGraph : IDisposable
{
    void SetScene(IReadOnlyList<LoadedModel> models);

    void PrepareEnvironment(IFrameEnvironment environment);

    void Run(FrameContext frame);
}
