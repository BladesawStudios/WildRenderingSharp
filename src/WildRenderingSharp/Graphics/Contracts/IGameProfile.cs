using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Rendering.Cameras;

namespace WildRenderingSharp.Graphics.Contracts;

/// <summary>
/// Everything that differs between games' shader interfaces: how the renderer's neutral frame data becomes the uniform blocks their
/// shaders read, where each is bound, and the stages of the frame.
/// </summary>
public interface IGameProfile
{
    string Name { get; }

    ShaderBindings Bindings { get; }

    IShaderSources ShaderSources { get; }

    Ubo Camera(string key, in CameraData camera);

    IReadOnlyList<Ubo> Lighting(in SceneLightingData lighting);

    IReadOnlyList<Ubo> Actor(in SkinningData actor);

    IReadOnlyList<UboSpec> InstancedActorBlocks { get; }

    IFrameGraph CreateFrameGraph(StageServices services);
}
