using System.Runtime.CompilerServices;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ARB;

namespace WildRenderingSharp.Gpu;

/// <summary>Draws the game's own programs the way they were written to be drawn: with an upper-left window origin.</summary>
internal static class ClipOrigin
{
    static readonly ConditionalWeakTable<GL, StrongBox<ArbClipControl?>> Extensions = new();

    public static bool Supported(GL gl) => Extension(gl) is not null;

    static ArbClipControl? Extension(GL gl) => Extensions.GetValue(gl, g =>
    {
        try { return new StrongBox<ArbClipControl?>(g.TryGetExtension(out ArbClipControl ext) ? ext : null); }
        catch { return new StrongBox<ArbClipControl?>(null); }
    }).Value;

    public static void Game(GL gl, bool on) =>
        Extension(gl)?.ClipControl((ARB)(on ? GLEnum.UpperLeft : GLEnum.LowerLeft), (ARB)GLEnum.NegativeOneToOne);
}
