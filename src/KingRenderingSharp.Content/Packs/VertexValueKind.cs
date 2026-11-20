namespace KingRenderingSharp.Content.Packs;

/// <summary>How the stored value of a vertex component becomes the value a shader reads.</summary>
/// <remarks>Normalized kinds become floats in 0 to 1 or -1 to 1, the ToFloat kinds the float of the same integer, and UInt and SInt keep the integer as raw bits.</remarks>
enum VertexValueKind
{
    UNorm,
    SNorm,
    UInt,
    SInt,
    Float,
    UIntToFloat,
    SIntToFloat,
}
