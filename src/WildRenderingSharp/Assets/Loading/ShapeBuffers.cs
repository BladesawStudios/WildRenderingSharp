using WildRenderingSharp.Assets.Manifests;

namespace WildRenderingSharp.Assets.Loading;

// A shape's vertex and index buffers with the layout of the vertices in them, which is what a vertex array is built from.
/// <summary>A shape's vertex and index buffers with the layout of the vertices in them, which is what a vertex array is built from.</summary>
sealed record ShapeBuffers(List<VertexLayoutEntry> Layout, int Stride, uint Vbo, uint Ibo, bool ConstantSkin);
