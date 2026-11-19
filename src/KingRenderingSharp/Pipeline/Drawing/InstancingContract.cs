namespace KingRenderingSharp.Pipeline.Drawing;

/// <summary>
/// What an instanced program and the code that draws it agree on: the storage buffer the per-placement data is read from and the
/// uniforms that locate one placement in it.
/// </summary>
internal static class InstancingContract
{
    public const uint InstanceBinding = 7;

    public const string FirstInstanceUniform = "krs_first_instance";
    public const string StrideUniform = "krs_instance_stride";
    public const string PaletteVec4sUniform = "krs_palette_vec4s";
    public const string PaletteRepeatUniform = "krs_palette_repeat";
}
