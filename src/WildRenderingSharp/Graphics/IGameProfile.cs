using WildRenderingSharp.Pipeline.Frame;

namespace WildRenderingSharp.Graphics;

/// <summary>
/// Everything that differs between games' shader interfaces: how the renderer's neutral frame data becomes the uniform blocks their
/// shaders read, where each is bound, and the stages of the frame.
/// </summary>
public interface IGameProfile
{
    string Name { get; }

    IWorldBasis World { get; }

    ShaderBindings Bindings { get; }

    IShaderSources ShaderSources { get; }

    /// <summary>The camera block, kept under <paramref name="key"/> so several variants can live side by side.</summary>
    UniformBlock Camera(string key, in CameraData camera);

    IReadOnlyList<UniformBlock> Lighting(in SceneLightingData lighting);

    /// <summary>The blocks one actor's shapes read: its bone palette and rigid transform.</summary>
    IReadOnlyList<UniformBlock> Actor(in SkinningData actor);

    /// <summary>Blocks bound as zeroes while an instanced batch draws, for shaders that still declare what instancing replaces.</summary>
    IReadOnlyList<UniformBlock> InstancedActorPlaceholders { get; }

    /// <summary>Builds the ordered stages of this game's frame.</summary>
    IFrameGraph CreateFrameGraph(FrameServices services);
}
