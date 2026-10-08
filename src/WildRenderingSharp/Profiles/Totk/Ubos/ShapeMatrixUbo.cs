using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>
/// TotK <c>ShpMtx</c> (the shape/model transform), binding 4, 256 bytes (16 vec4 slots - only rows 0-2 are ever populated, the rest
/// is reserved padding the shader never reads). Binding 4 is REUSED later in the same frame for <c>agl_hdr_compose</c>'s own
/// <c>cContext.cParam</c> (see <see cref="Shaders.HdrComposeParamsUbo"/> in <c>WildRenderingSharp</c>) - that is how the original
/// shaders are laid out, not a bug. The pipeline orchestrator is responsible for binding this block during the G-buffer/shadow
/// passes and swapping to <c>HdrComposeParamsUbo</c> only for the final tonemap draw.
/// </summary>
public sealed class ShapeMatrixUbo : IUboBlock
{
    public const int ByteSize = 256;

    readonly Std140Block _block = new(ByteSize);

    public string Name => "ShpMtx";
    public const uint Binding = TotkBindings.ShapeMatrix;
    public int BindingIndex => (int)Binding;

    /// <summary>Rows 0-2 = the model/shape transform (mat3x4); everything else stays zero.</summary>
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
