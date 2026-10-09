namespace WildRenderingSharp.Graphics.Contracts;

/// <summary>How a game's decompiled shader source is made fit to compile on desktop GL.</summary>
internal interface IShaderSources
{
    string Clean(string source);

    string CorrectForwardFragment(string fragmentSource);

    string? Instance(string cleanedVertexSource);
}
