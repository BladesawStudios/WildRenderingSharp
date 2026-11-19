using KingRenderingSharp.Graphics.Contracts;
using KingRenderingSharp.Graphics.Data;
using KingRenderingSharp.Graphics.Ubos;
using KingRenderingSharp.Pipeline.Frame;
using KingRenderingSharp.Profiles.Totk.Shaders;
using KingRenderingSharp.Profiles.Totk.Ubos;

namespace KingRenderingSharp.Profiles.Totk;

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
