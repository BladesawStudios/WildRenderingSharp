namespace WildRenderingSharp.Pipeline.Merged;

/// <summary>What the merged-draw shaders and the code that feeds them agree on: the sizes of what is looked up per draw and the buffers it is looked up in.</summary>
internal static class MergedLayout
{
    // The vec4s a shape's material takes: no game program reads past the 42nd, and a program that does is drawn the ordinary way.
    public const int MaterialVec4s = 64;

    // The textures one shape-and-program can bind, by the unit the shader says, each a 64-bit bindless handle.
    public const int HandleSlots = 32;

    // Where an instance's baked-lighting atlas handle sits among its vec4s, and where its palette starts.
    public const int AtlasOffset = 4, PaletteOffset = 5;

    public const uint MaterialBinding = 1, HandleBinding = 2, DrawBinding = 3;

    public const string DrawBaseUniform = "wrs_draw_base";

    // Shape flags in a draw: the shape takes the baked-lighting atlas from each instance.
    public const int UsesInstanceAtlas = 1;
}
