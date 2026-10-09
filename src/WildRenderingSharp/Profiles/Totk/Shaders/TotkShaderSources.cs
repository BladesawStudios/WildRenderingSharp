using WildRenderingSharp.Graphics.Contracts;

namespace WildRenderingSharp.Profiles.Totk.Shaders;

/// <summary>How TotK's decompiled GLSL is cleaned, corrected and instanced.</summary>
internal sealed class TotkShaderSources : IShaderSources
{
    public string Clean(string source) => TotkGlsl.Clean(source);

    public string CorrectForwardFragment(string fragmentSource) => KnownDecompilerCorrections.Apply(fragmentSource);

    public string? Instance(string cleanedVertexSource) => InstancedShaderPatch.Apply(cleanedVertexSource);
}
