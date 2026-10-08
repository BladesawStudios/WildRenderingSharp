using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Shaders;

public sealed class TotkShaderSources : IShaderSources
{
    public string Clean(string source) => GlslSanitizer.Clean(source);

    public string CorrectForwardFragment(string fragmentSource) => KnownDecompilerCorrections.Apply(fragmentSource);

    public string? Instance(string cleanedVertexSource) => InstancedShaderPatch.Apply(cleanedVertexSource);
}
