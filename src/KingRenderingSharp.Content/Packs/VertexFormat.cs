using BfShaderSharp.Errors;

namespace KingRenderingSharp.Content.Packs;

/// <summary>What a vertex attribute's format word of the model file means: how many components, how wide, and how a value is interpreted.</summary>
/// <remarks>
/// The low byte of the word picks the layout (8, 16 or 32 bits a component, 1 to 4 components, or the packed 10-10-10-2) and the high byte the interpretation:
/// 0 normalized unsigned, 1 unsigned integer, 2 normalized signed, 3 signed integer, 8 float (or an unsigned integer converted to float for the integer layouts)
/// and 0xA a signed integer converted to float. A word this table does not list throws <see cref="BfShaderUnsupportedException"/>.
/// </remarks>
sealed record VertexFormat(int Components, int ComponentBits, VertexValueKind Kind, bool Packed1010102 = false)
{
    public int ByteSize => Packed1010102 ? 4 : Components * ComponentBits / 8;

    // Layout byte to (components, bits, whether the layout is a float layout when the interpretation byte is 8).
    static readonly Dictionary<uint, (int Components, int Bits, bool FloatLayout)> Layouts = new()
    {
        [0x00] = (1, 8, false), [0x02] = (1, 16, false), [0x03] = (1, 16, true), [0x04] = (2, 8, false), [0x05] = (1, 32, false), [0x06] = (1, 32, true),
        [0x07] = (2, 16, false), [0x08] = (2, 16, true), [0x0a] = (4, 8, false), [0x0c] = (2, 32, false), [0x0d] = (2, 32, true), [0x0e] = (4, 16, false),
        [0x0f] = (4, 16, true), [0x10] = (3, 32, false), [0x11] = (3, 32, true), [0x12] = (4, 32, false), [0x13] = (4, 32, true),
    };

    public static VertexFormat FromWord(uint word)
    {
        uint layout = word & 0xff, interpretation = word >> 8;
        if (layout == 0x0b) return new VertexFormat(4, 10, KindOf(interpretation, false, word), Packed1010102: true);
        if (!Layouts.TryGetValue(layout, out var l)) throw new BfShaderUnsupportedException($"Vertex format 0x{word:x} is not read");
        return new VertexFormat(l.Components, l.Bits, KindOf(interpretation, l.FloatLayout, word));
    }

    static VertexValueKind KindOf(uint interpretation, bool floatLayout, uint word) => interpretation switch
    {
        0 => VertexValueKind.UNorm,
        1 => VertexValueKind.UInt,
        2 => VertexValueKind.SNorm,
        3 => VertexValueKind.SInt,
        8 => floatLayout ? VertexValueKind.Float : VertexValueKind.UIntToFloat,
        0xa => VertexValueKind.SIntToFloat,
        _ => throw new BfShaderUnsupportedException($"Vertex format 0x{word:x} is not read"),
    };
}
