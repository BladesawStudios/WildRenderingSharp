using BfresLibrary;
using BfresLibrary.Helpers;
using Syroot.BinaryData;

namespace WildRenderingSharp.Preparation.Totk.Effects;

/// <summary>A particle mesh as the live half reads it: a position, a vertex colour and a texture coordinate per vertex, and the indices.</summary>
internal sealed record TotkEffectMesh(float[] Positions, float[] Colors, float[] Uvs, uint[] Indices, bool TriangleStrip)
{
    const string PositionAttribute = "_p0", ColorAttribute = "_c0", UvAttribute = "_u0";

    public int VertexCount => Positions.Length / 3;

    // How far the mesh reaches along an axis (0, 1 or 2), unscaled.
    public float Extent(int axis)
    {
        float low = float.MaxValue, high = float.MinValue;
        for (int vertex = axis; vertex < Positions.Length; vertex += 3)
        {
            low = MathF.Min(low, Positions[vertex]);
            high = MathF.Max(high, Positions[vertex]);
        }
        return high - low;
    }

    // The model at the given place in the effect file's G3D resource.
    public static TotkEffectMesh Read(byte[] resource, int modelIndex)
    {
        var file = new ResFile(new MemoryStream(resource));
        Model model = file.Models[modelIndex];
        Shape shape = model.Shapes[0];
        var vertices = new VertexBufferHelper(model.VertexBuffers[shape.VertexBufferIndex], ByteOrder.LittleEndian);
        var position = vertices.Attributes.FirstOrDefault(a => a.Name == PositionAttribute)
            ?? throw new InvalidDataException($"Model '{model.Name}' has no position attribute.");
        var color = vertices.Attributes.FirstOrDefault(a => a.Name == ColorAttribute);
        var uv = vertices.Attributes.FirstOrDefault(a => a.Name == UvAttribute);

        var positions = new float[position.Data.Length * 3];
        var colors = new float[position.Data.Length * 4];
        var uvs = new float[position.Data.Length * 2];
        for (int v = 0; v < position.Data.Length; v++)
        {
            positions[(v * 3) + 0] = position.Data[v].X;
            positions[(v * 3) + 1] = position.Data[v].Y;
            positions[(v * 3) + 2] = position.Data[v].Z;
            // A model without vertex colours is opaque white.
            colors[(v * 4) + 0] = color?.Data[v].X ?? 1f;
            colors[(v * 4) + 1] = color?.Data[v].Y ?? 1f;
            colors[(v * 4) + 2] = color?.Data[v].Z ?? 1f;
            colors[(v * 4) + 3] = color?.Data[v].W ?? 1f;
            uvs[(v * 2) + 0] = uv?.Data[v].X ?? 0f;
            uvs[(v * 2) + 1] = uv?.Data[v].Y ?? 0f;
        }

        Mesh mesh = shape.Meshes[0];
        return new TotkEffectMesh(positions, colors, uvs, mesh.GetIndices().ToArray(), mesh.PrimitiveType.ToString().Contains("Strip", StringComparison.Ordinal));
    }

    public void Write(string path)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        foreach (float value in Positions) writer.Write(value);
        foreach (float value in Colors) writer.Write(value);
        foreach (float value in Uvs) writer.Write(value);
        foreach (uint index in Indices) writer.Write(index);
    }
}
