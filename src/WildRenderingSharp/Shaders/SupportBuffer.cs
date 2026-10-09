using WildRenderingSharp.Graphics.Ubos;

namespace WildRenderingSharp.Shaders;

/// <summary>
/// The decompiler's own uniform block, which every decompiled shader declares whatever game it came from: the alpha test, the viewport, and
/// a 73-entry render scale array.
/// </summary>
internal static class SupportBuffer
{
    const int AlphaTestOffset = 36;
    const int RenderScaleOffset = 56;
    const int RenderScaleCount = 73;

    public static readonly UboSpec Spec = new("support_buffer", 0, 512);

    // The block as every shader wants it, which never changes.
    public static Ubo Block { get; } = Build();

    static Ubo Build()
    {
        var block = new UboWriter(Spec);
        block.SetAt(AlphaTestOffset, 1f);
        for (int i = 0; i < RenderScaleCount; i++)
            block.SetAt(RenderScaleOffset + i * 4, 1f);
        return block.ToUbo("support");
    }
}
