using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// What an instanced program and the code that draws it agree on: the storage buffer the per-placement data is read from and the
/// uniforms that locate one placement in it.
/// </summary>
public static class InstancingContract
{
    public const uint InstanceBinding = 7;

    public const string FirstInstanceUniform = "wrs_first_instance";
    public const string StrideUniform = "wrs_instance_stride";
    public const string PaletteVec4sUniform = "wrs_palette_vec4s";
    public const string PaletteRepeatUniform = "wrs_palette_repeat";

    public static bool BaseInstance { get; set; }

    public static void Detect(GL gl) => BaseInstance = gl.IsExtensionPresent("GL_ARB_shader_draw_parameters");
}
