
namespace WildRenderingSharp.Profiles.Botw;

/// <summary>
/// Where the renderer binds each uniform block BotW's translated shaders read. The shaders' own numbering collides, so <see
/// cref="Shaders.BotwShaderSources"/> renumbers them to these.
/// </summary>
internal static class BotwBindings
{
    public const uint Camera = 1;
    public const uint Bones = 2;
    public const uint Environment = 6;
    public const uint Material = 8;
    public const uint SceneMaterial = 10;

    public const uint Orphan = 30;
}
