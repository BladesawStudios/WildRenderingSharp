using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Botw.Shaders;

namespace WildRenderingSharp.Profiles.Botw;

public sealed class BotwProfile : IGameProfile
{
    public string Name => "Breath of the Wild";

    public IShaderSources ShaderSources { get; } = new BotwShaderSources();

    public ShaderBindings Bindings { get; } = new(BotwBindings.Camera, BotwBindings.Environment, BotwBindings.Material);

    public Ubo Camera(string key, in CameraData camera) => BotwUniforms.Camera(key, camera);

    public IReadOnlyList<Ubo> Lighting(in SceneLightingData lighting) => BotwUniforms.Lighting(lighting);

    public IReadOnlyList<Ubo> Actor(in SkinningData actor) => BotwUniforms.Actor(actor);

    public IReadOnlyList<UboSpec> InstancedActorBlocks => BotwUniforms.InstancedBlocks;

    public IFrameGraph CreateFrameGraph(StageServices services) => new BotwFrameGraph(services);
}
