using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Botw.Shaders;

namespace WildRenderingSharp.Profiles.Botw;

public sealed class BotwProfile : IGameProfile
{
    public string Name => "Breath of the Wild";

    public IShaderSources ShaderSources { get; } = new BotwShaderSources();

    public ShaderBindings Bindings { get; } = new(BotwBindings.Camera, BotwBindings.Environment, BotwBindings.Material);

    public UniformBlock Camera(string key, in CameraData camera) => BotwUniforms.Camera(key, camera);

    public IReadOnlyList<UniformBlock> Lighting(in SceneLightingData lighting) => BotwUniforms.Lighting(lighting);

    public IReadOnlyList<UniformBlock> Actor(in SkinningData actor) => BotwUniforms.Actor(actor);

    public IReadOnlyList<UniformBlock> InstancedActorPlaceholders => BotwUniforms.Placeholders;

    public IFrameGraph CreateFrameGraph(FrameServices services) => new BotwFrameGraph(services);
}
