using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// What an instanced program and the code that draws it agree on: the storage buffer the per-placement data is read from and the
/// uniforms that locate one placement in it. A profile's shader source transform produces programs that follow it.
/// </summary>
public static class InstancingContract
{
    /// <summary>The storage-buffer binding the instance data is read from. The decompiled shaders themselves use binding 0.</summary>
    public const uint InstanceBinding = 7;

    public const string FirstInstanceUniform = "wrs_first_instance";
    public const string StrideUniform = "wrs_instance_stride";
    public const string PaletteVec4sUniform = "wrs_palette_vec4s";
    public const string PaletteRepeatUniform = "wrs_palette_repeat";

    /// <summary>
    /// Whether instances are found through <c>gl_BaseInstance</c> (ARB_shader_draw_parameters) as well as the first-instance
    /// uniform, which lets every visible run of a shape go out in one multi-draw. Set once a context is known to have it, before
    /// any instanced program is built.
    /// </summary>
    public static bool BaseInstance { get; set; }

    public static void Detect(GL gl) => BaseInstance = gl.IsExtensionPresent("GL_ARB_shader_draw_parameters");
}
