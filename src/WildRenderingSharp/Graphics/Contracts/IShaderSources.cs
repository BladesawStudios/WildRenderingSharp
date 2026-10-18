namespace WildRenderingSharp.Graphics.Contracts;

/// <summary>How a game's decompiled shader source is made fit to compile on desktop GL.</summary>
internal interface IShaderSources
{
    string Clean(string source);

    string CorrectForwardFragment(string fragmentSource);

    string? Instance(string cleanedVertexSource, bool baseInstance);

    // The pair made to draw any shape of any batch from one multi-draw, or null when this pair cannot be.
    (string Vertex, string Fragment)? Merge(string cleanedVertexSource, string cleanedFragmentSource);
}
