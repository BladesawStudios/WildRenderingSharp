using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Totk.Shaders;

namespace WildRenderingSharp.Profiles.Totk;

public sealed class TotkProfile : IGameProfile
{
    public string Name => "Tears of the Kingdom";

    public IWorldBasis World => YUpWorldBasis.Instance;

    public IShaderSources ShaderSources { get; } = new TotkShaderSources();

    public ShaderBindings Bindings { get; } = new(TotkBindings.Camera, TotkBindings.Environment, TotkBindings.Material);

    public UniformBlock Camera(string key, in CameraData camera) => TotkCameraUniforms.Build(World, key, camera);

    public IReadOnlyList<UniformBlock> Lighting(in SceneLightingData lighting) => TotkLightingUniforms.Build(World, lighting);

    public IReadOnlyList<UniformBlock> Actor(in SkinningData actor) => TotkActorUniforms.Build(World, actor);

    public IReadOnlyList<UniformBlock> InstancedActorPlaceholders => TotkActorUniforms.InstancedPlaceholders;
}
