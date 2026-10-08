using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>
/// TotK <c>ShpMtx</c> (the shape/model transform), binding 4, 256 bytes (16 vec4 slots - only rows 0-2 are ever populated, the rest
/// is reserved padding the shader never reads).
/// </summary>
public sealed class ShapeMatrixUbo : IUboBlock
{
    public const int ByteSize = 256;

    readonly Std140Block _block = new(ByteSize);

    public string Name => "ShpMtx";
    public const uint Binding = TotkBindings.ShapeMatrix;
    public int BindingIndex => (int)Binding;

    public static ShapeMatrixUbo BuildFromModelMatrix(ReadOnlySpan<Vector4> modelRows)
    {
        if (modelRows.Length != 3)
            throw new ArgumentException("model transform must have exactly 3 rows (mat3x4)", nameof(modelRows));
        var shp = new ShapeMatrixUbo();
        shp._block.WriteRows(0, modelRows);
        return shp;
    }

    public byte[] ToByteArray() => _block.ToByteArray();
}
