using KingRenderingSharp.Graphics.Contracts;
using KingRenderingSharp.Graphics.Data;
using KingRenderingSharp.Graphics.Ubos;
using KingRenderingSharp.Pipeline.Frame;
using KingRenderingSharp.Profiles.Botw.Shaders;
using KingRenderingSharp.Profiles.Botw.Ubos;

namespace KingRenderingSharp.Profiles.Botw;

/// <summary>The Breath of the Wild profile: its bindings, shader sources and frame graph.</summary>
internal sealed class BotwProfile : IGameProfile
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
