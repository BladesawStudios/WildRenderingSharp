namespace WildRenderingSharp.Graphics;

/// <summary>How a game's decompiled shader source is made fit to compile on desktop GL.</summary>
public interface IShaderSources
{
    /// <summary>Rewrites a decompiled stage so a desktop driver accepts it.</summary>
    string Clean(string source);

    /// <summary>Repairs known decompiler mistakes in a forward program's fragment stage.</summary>
    string CorrectForwardFragment(string fragmentSource);

    /// <summary>The cleaned vertex stage rewritten to draw many placements per call, or null when there is nothing to wrap.</summary>
    string? Instance(string cleanedVertexSource);
}
