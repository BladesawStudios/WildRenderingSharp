using KingRenderingSharp.Content.Graphics.Contracts;
using KingRenderingSharp.Content.Graphics.Data;
using KingRenderingSharp.Content.Graphics.Ubos;
using KingRenderingSharp.Graph.Pipeline.Frame;
using KingRenderingSharp.Games.Botw.Shaders;
using KingRenderingSharp.Games.Botw.Ubos;

namespace KingRenderingSharp.Games.Botw;

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
