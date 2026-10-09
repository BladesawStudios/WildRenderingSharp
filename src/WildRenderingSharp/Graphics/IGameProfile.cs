using WildRenderingSharp.Pipeline.Frame;

namespace WildRenderingSharp.Graphics;

/// <summary>
/// Everything that differs between games' shader interfaces: how the renderer's neutral frame data becomes the uniform blocks their
/// shaders read, where each is bound, and the stages of the frame.
/// </summary>
public interface IGameProfile
{
    string Name { get; }

    ShaderBindings Bindings { get; }

    IShaderSources ShaderSources { get; }

    UniformBlock Camera(string key, in CameraData camera);

    IReadOnlyList<UniformBlock> Lighting(in SceneLightingData lighting);

    IReadOnlyList<UniformBlock> Actor(in SkinningData actor);

    IReadOnlyList<UniformBlock> InstancedActorPlaceholders { get; }

    IFrameGraph CreateFrameGraph(FrameServices services);
}
