using KingRenderingSharp.Gpu;

namespace KingRenderingSharp.Graph.Pipeline.Merged;

/// <summary>Whether the context can draw from the merged buffers, which needs textures that need no unit and a shader that can tell which draw of a multi-draw it is.</summary>
internal static class MergedSupport
{
    sealed class State
    {
        public bool? Available;
        public GpuBindless? Bindless;
    }

    public static bool Available(GpuApi gl)
    {
        var state = gl.State<State>();
        return state.Available ??= Check(gl, state);
    }

    public static GpuBindless Bindless(GpuApi gl) =>
        gl.State<State>().Bindless ?? throw new InvalidOperationException("The context has no bindless textures.");

    static bool Check(GpuApi gl, State state)
    {
        if (!gl.IsExtensionPresent("GL_ARB_shader_draw_parameters") || gl.Bindless is not { } bindless)
            return false;
        state.Bindless = bindless;
        return true;
    }
}
