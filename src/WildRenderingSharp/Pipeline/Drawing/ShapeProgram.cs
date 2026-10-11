
namespace WildRenderingSharp.Pipeline.Drawing;

/// <summary>Which of a shape's three programs a pass draws it with.</summary>
internal enum ShapeProgram
{
    GBuffer,
    ZOnly,
    Forward,
}
