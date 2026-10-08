using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>A game's ordered frame, with whatever scene and environment state its stages share.</summary>
public interface IFrameGraph : IDisposable
{
    /// <summary>Called whenever the set of loaded models changes.</summary>
    void SetScene(IReadOnlyList<LoadedModel> models);

    /// <summary>Does the work that depends only on the environment, never the camera or the scene. Safe to call every frame.</summary>
    void PrepareEnvironment(IFrameEnvironment environment, LightingContext lighting);

    void Run(FrameContext frame);
}
