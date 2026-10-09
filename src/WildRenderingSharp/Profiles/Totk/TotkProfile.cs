using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Shaders;

namespace WildRenderingSharp.Profiles.Totk;

public sealed class TotkProfile : IGameProfile
{
    public string Name => "Tears of the Kingdom";

    public IShaderSources ShaderSources { get; } = new TotkShaderSources();

    public ShaderBindings Bindings { get; } = new(TotkBindings.Camera, TotkBindings.Environment, TotkBindings.Material);

    public UniformBlock Camera(string key, in CameraData camera) => TotkCameraUniforms.Build(key, camera);

    public IReadOnlyList<UniformBlock> Lighting(in SceneLightingData lighting) => TotkLightingUniforms.Build(lighting);

    public IReadOnlyList<UniformBlock> Actor(in SkinningData actor) => TotkActorUniforms.Build(actor);

    public IReadOnlyList<UniformBlock> InstancedActorPlaceholders => TotkActorUniforms.InstancedPlaceholders;

    public IFrameGraph CreateFrameGraph(FrameServices services) => new TotkFrameGraph(services);
}
