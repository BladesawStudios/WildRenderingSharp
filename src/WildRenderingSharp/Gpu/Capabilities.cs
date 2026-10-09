using Silk.NET.OpenGL;

namespace WildRenderingSharp.Gpu;

/// <summary>What the GL context can do beyond the version it was created with, asked of the driver once.</summary>
internal static class Capabilities
{
    sealed class Support
    {
        public bool? BaseInstance;
    }

    // Whether a shader can read gl_BaseInstance, which a multi-draw uses to say where each run's instances start.
    public static bool SupportsBaseInstance(this GL gl) =>
        ContextState<Support>.For(gl).BaseInstance ??= gl.IsExtensionPresent("GL_ARB_shader_draw_parameters");
}
