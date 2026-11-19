using System.Runtime.CompilerServices;

namespace KingRenderingSharp.Gpu;

/// <summary>Draws the game's own programs the way they were written to be drawn: with an upper-left window origin.</summary>
internal static class ClipOrigin
{
    public static bool Supported(GpuApi gl) => gl.SupportsClipControl;

    public static void Game(GpuApi gl, bool on)
    {
        if (gl.SupportsClipControl)
            gl.SetClipControl(on ? GLEnum.UpperLeft : GLEnum.LowerLeft, GLEnum.NegativeOneToOne);
    }
}
