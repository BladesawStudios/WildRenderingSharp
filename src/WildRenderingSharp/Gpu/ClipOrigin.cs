using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ARB;

namespace WildRenderingSharp.Gpu;

/// <summary>Draws the game's own programs the way they were written to be drawn: with an upper-left window origin.</summary>
internal static class ClipOrigin
{
    sealed class Holder
    {
        public ArbClipControl? Extension;
        public bool Looked;
    }

    public static bool Supported(GL gl) => Extension(gl) is not null;

    static ArbClipControl? Extension(GL gl)
    {
        var holder = ContextState<Holder>.For(gl);
        if (!holder.Looked)
        {
            try { holder.Extension = gl.TryGetExtension(out ArbClipControl ext) ? ext : null; }
            catch { holder.Extension = null; }
            holder.Looked = true;
        }
        return holder.Extension;
    }

    public static void Game(GL gl, bool on) =>
        Extension(gl)?.ClipControl((ARB)(on ? GLEnum.UpperLeft : GLEnum.LowerLeft), (ARB)GLEnum.NegativeOneToOne);
}
