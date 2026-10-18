using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ARB;
using WildRenderingSharp.Gpu;

namespace WildRenderingSharp.Pipeline.Merged;

/// <summary>Whether the context can draw from the merged buffers, which needs textures that need no unit and a shader that can tell which draw of a multi-draw it is.</summary>
internal static class MergedSupport
{
    sealed class State
    {
        public bool? Available;
        public ArbBindlessTexture? Bindless;
    }

    public static bool Available(GL gl)
    {
        var state = ContextState<State>.For(gl);
        return state.Available ??= Check(gl, state);
    }

    public static ArbBindlessTexture Bindless(GL gl) =>
        ContextState<State>.For(gl).Bindless ?? throw new InvalidOperationException("The context has no bindless textures.");

    static bool Check(GL gl, State state)
    {
        if (!gl.IsExtensionPresent("GL_ARB_shader_draw_parameters") || !gl.TryGetExtension(out ArbBindlessTexture bindless))
            return false;
        state.Bindless = bindless;
        return true;
    }
}
