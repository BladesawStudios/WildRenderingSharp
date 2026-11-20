namespace KingRenderingSharp.Content.Packs;

/// <summary>The shader parameter types of the model file by number: their names, which the material parameter layouts carry, and their sizes in bytes.</summary>
static class PackShaderParams
{
    public const byte Float4 = 15, Float4x4 = 27, Srt2D = 28, Srt3D = 29, TexSrt = 30, TexSrtEx = 31;

    static readonly string[] Names =
    [
        "Bool", "Bool2", "Bool3", "Bool4", "Int", "Int2", "Int3", "Int4", "UInt", "UInt2", "UInt3", "UInt4",
        "Float", "Float2", "Float3", "Float4", "Reserved2", "Float2x2", "Float2x3", "Float2x4",
        "Reserved3", "Float3x2", "Float3x3", "Float3x4", "Reserved4", "Float4x2", "Float4x3", "Float4x4",
        "Srt2D", "Srt3D", "TexSrt", "TexSrtEx",
    ];

    public static string NameOf(byte type) => type < Names.Length ? Names[type] : type.ToString();

    // Vectors are four bytes a component; a matrix is four bytes a column and row; the rest have a size of their own.
    public static int SizeOf(byte type)
    {
        if (type <= Float4) return 4 * ((type & 3) + 1);
        if (type <= Float4x4)
        {
            int columns = (type & 3) + 1;
            int rows = ((type - 16) >> 2) + 2;
            return 4 * columns * rows;
        }

        return type switch { Srt2D => 20, Srt3D => 36, TexSrt => 24, TexSrtEx => 28, _ => 16 };
    }
}
