using System.Buffers.Binary;

namespace KingRenderingSharp.Content.Packs;

/// <summary>Decodes one stored vertex attribute into four components as a shader reads them.</summary>
/// <remarks>
/// Float and normalized kinds come out as floats, the integer kinds as their value (an index or a count reads as a whole number). Components the format lacks are 0,
/// except the fourth of a float format, which is 1 as for a position.
/// </remarks>
static class VertexDecoder
{
    public static void Decode(VertexFormat format, ReadOnlySpan<byte> source, Span<float> output)
    {
        output[0] = output[1] = output[2] = 0;
        output[3] = format.Kind == VertexValueKind.Float ? 1f : 0f;
        for (int i = 0; i < format.Components; i++)
            output[i] = format.Packed1010102 ? Packed(format.Kind, BinaryPrimitives.ReadUInt32LittleEndian(source), i) : Component(format, source, i);
        if (format.Packed1010102) output[3] = Packed(format.Kind, BinaryPrimitives.ReadUInt32LittleEndian(source), 3);
    }

    static float Component(VertexFormat format, ReadOnlySpan<byte> source, int index)
    {
        int bytes = format.ComponentBits / 8;
        ReadOnlySpan<byte> at = source[(index * bytes)..];
        bool signed = format.Kind is VertexValueKind.SNorm or VertexValueKind.SInt or VertexValueKind.SIntToFloat;
        if (format.Kind == VertexValueKind.Float)
            return bytes switch
            {
                2 => (float)BinaryPrimitives.ReadHalfLittleEndian(at),
                _ => BinaryPrimitives.ReadSingleLittleEndian(at),
            };

        long value = bytes switch
        {
            1 => signed ? (sbyte)at[0] : at[0],
            2 => signed ? BinaryPrimitives.ReadInt16LittleEndian(at) : BinaryPrimitives.ReadUInt16LittleEndian(at),
            _ => signed ? BinaryPrimitives.ReadInt32LittleEndian(at) : BinaryPrimitives.ReadUInt32LittleEndian(at),
        };
        return Convert(format.Kind, value, format.ComponentBits);
    }

    // Ten-bit components with a two-bit alpha; for a signed-normalized word only the alpha stays unsigned.
    static float Packed(VertexValueKind kind, uint word, int index)
    {
        int bits = index == 3 ? 2 : 10;
        uint raw = word >> (index * 10) & (index == 3 ? 3u : 0x3ffu);
        bool signed = kind is VertexValueKind.SNorm or VertexValueKind.SInt && index != 3;
        long value = signed && (raw & 1u << bits - 1) != 0 ? (long)raw - (1L << bits) : raw;
        return Convert(index == 3 && kind == VertexValueKind.SNorm ? VertexValueKind.UNorm : kind, value, bits);
    }

    static float Convert(VertexValueKind kind, long value, int bits) => kind switch
    {
        VertexValueKind.UNorm => (float)value / ((1L << bits) - 1),
        VertexValueKind.SNorm => Math.Max(-1f, (float)value / ((1L << bits - 1) - 1)),
        _ => value,
    };
}
