using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Botw.Shaders;

namespace WildRenderingSharp.Profiles.Botw;

public sealed class BotwProfile : IGameProfile
{
    public string Name => "Breath of the Wild";

    public IWorldBasis World => YUpWorldBasis.Instance;

    public IShaderSources ShaderSources { get; } = new BotwShaderSources();

    public ShaderBindings Bindings { get; } = new(BotwBindings.Camera, BotwBindings.Environment, BotwBindings.Material);

    public UniformBlock Camera(string key, in CameraData camera) => BotwUniforms.Camera(World, key, camera);

    public IReadOnlyList<UniformBlock> Lighting(in SceneLightingData lighting) => [];

    public IReadOnlyList<UniformBlock> Actor(in SkinningData actor) => BotwUniforms.Actor(World, actor);

    public IReadOnlyList<UniformBlock> InstancedActorPlaceholders => BotwUniforms.Placeholders;

    public IFrameGraph CreateFrameGraph(FrameServices services) => new BotwFrameGraph(services);
}
