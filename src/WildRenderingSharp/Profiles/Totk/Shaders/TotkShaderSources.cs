using WildRenderingSharp.Graphics.Contracts;

namespace WildRenderingSharp.Profiles.Totk.Shaders;

/// <summary>How TotK's decompiled GLSL is cleaned, corrected and instanced.</summary>
internal sealed class TotkShaderSources : IShaderSources
{
    public string Clean(string source) => VertexDissolveFix.Apply(TotkGlsl.Clean(source));

    public string CorrectForwardFragment(string fragmentSource) => KnownDecompilerCorrections.Apply(fragmentSource);

    public string? Instance(string cleanedVertexSource, bool baseInstance) => InstancedShaderPatch.Apply(cleanedVertexSource, baseInstance);

    public (string Vertex, string Fragment)? Merge(string cleanedVertexSource, string cleanedFragmentSource) =>
        MergedShaderPatch.Apply(cleanedVertexSource, cleanedFragmentSource);
}
