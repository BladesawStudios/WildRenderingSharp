namespace WildRenderingSharp.Assets;

// A shape's vertex and index buffers with the layout of the vertices in them, which is what a vertex array is built from.
sealed record ShapeBuffers(List<VertexLayoutEntry> Layout, int Stride, uint Vbo, uint Ibo, bool ConstantSkin);
