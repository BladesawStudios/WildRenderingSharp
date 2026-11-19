using KingRenderingSharp.Content.Graphics.Contracts;
using KingRenderingSharp.Content.Graphics.Data;
using KingRenderingSharp.Content.Graphics.Ubos;
using KingRenderingSharp.Graph.Pipeline.Frame;
using KingRenderingSharp.Games.Totk.Shaders;
using KingRenderingSharp.Games.Totk.Ubos;

namespace KingRenderingSharp.Games.Totk;

/// <summary>The Tears of the Kingdom profile: its bindings, uniform builders, shader sources and frame graph.</summary>
internal sealed class TotkProfile : IGameProfile
{
    public string Name => "Tears of the Kingdom";

    public IShaderSources ShaderSources { get; } = new TotkShaderSources();

    public ShaderBindings Bindings { get; } = new(TotkBindings.Camera, TotkBindings.Environment, TotkBindings.Material);

    public Ubo Camera(string key, in CameraData camera) => TotkCameraUniforms.Build(key, camera);

    public IReadOnlyList<Ubo> Lighting(in SceneLightingData lighting) => TotkLightingUniforms.Build(lighting);

    public IReadOnlyList<Ubo> Actor(in SkinningData actor) => TotkActorUniforms.Build(actor);

    public IReadOnlyList<UboSpec> InstancedActorBlocks => TotkActorUniforms.InstancedBlocks;

    public IFrameGraph CreateFrameGraph(StageServices services) => new TotkFrameGraph(services);
}
