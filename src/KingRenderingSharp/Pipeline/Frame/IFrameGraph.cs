using KingRenderingSharp.Assets;
using KingRenderingSharp.Graphics.Contracts;

namespace KingRenderingSharp.Pipeline.Frame;

/// <summary>A game's ordered frame, with whatever scene and environment state its stages share.</summary>
internal interface IFrameGraph : IDisposable
{
    void SetScene(IReadOnlyList<LoadedModel> models);

    void PrepareEnvironment(IFrameEnvironment environment);

    void Run(FrameContext frame);
}
