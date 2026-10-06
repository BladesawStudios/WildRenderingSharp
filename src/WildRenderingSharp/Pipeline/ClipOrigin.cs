using System.Runtime.CompilerServices;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ARB;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Draws the game's own programs the way they were written to be drawn: with an upper-left
/// window origin.
/// </summary>
/// <remarks>
/// <para>
/// NVN's framebuffer origin is the upper left, and the decompiled programs (through Ryujinx, which
/// emulates it with <c>glClipControl(GL_UPPER_LEFT)</c>) assume it: a program working out where on
/// screen a vertex lands - water reading the scene behind it, terrain reading the G-buffer under
/// it - writes <c>(w - y) / 2</c> for that. The renderer used to get the same picture in memory by
/// flipping the projection instead, which every such formula then reads upside down: refraction
/// mirrored, terrain fading into the sky and laying mirrored actors over itself.
/// </para>
/// <para>
/// So the passes that draw game geometry into the G-buffer's orientation (G-buffer, terrain,
/// scene-colour shapes, forward) draw with the plain projection and this origin. What lands in
/// memory is identical to before; only the programs' own screen-space reads change, to correct.
/// </para>
/// </remarks>
internal static class ClipOrigin
{
    static readonly ConditionalWeakTable<GL, StrongBox<ArbClipControl?>> Extensions = new();

    /// <summary>Whether this context can - every GL 4.5 driver, and most 4.3 ones through ARB_clip_control.</summary>
    public static bool Supported(GL gl) => Extension(gl) is not null;

    static ArbClipControl? Extension(GL gl) => Extensions.GetValue(gl, g =>
    {
        try { return new StrongBox<ArbClipControl?>(g.TryGetExtension(out ArbClipControl ext) ? ext : null); }
        catch { return new StrongBox<ArbClipControl?>(null); }
    }).Value;

    /// <summary>Sets the upper-left origin the game's programs expect, or puts GL's own back.</summary>
    public static void Game(GL gl, bool on) =>
        Extension(gl)?.ClipControl((ARB)(on ? GLEnum.UpperLeft : GLEnum.LowerLeft), (ARB)GLEnum.NegativeOneToOne);
}
