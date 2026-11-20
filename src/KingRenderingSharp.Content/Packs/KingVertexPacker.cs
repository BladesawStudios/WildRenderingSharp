using System.Numerics;
using BfShaderSharp.Errors;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;

namespace KingRenderingSharp.Content.Packs;

/// <summary>Packs a shape's streams into the 192-byte vertex and its levels of detail into one index list.</summary>
/// <remarks>
/// The attributes are the model file's: _p0 position, _n0 normal, _t0 tangent, _u0 to _u1 texture coordinates, _c0 colour, _w0 and _w1 weights, _i0 and _i1 bone
/// indices and bake the lightmap coordinates. A missing attribute reads as the neutral value (normal up, tangent along x, colour white, full weight on slot 0). Only a
/// rigid shape (skin count 0) is baked: its positions, normals and tangents are carried by its bone's world matrix. A shape with skin count 1 or more keeps its bone
/// indices, which are palette slots the GPU skins with, and its positions as stored.
/// </remarks>
static class KingVertexPacker
{
    const uint Triangles = 4;

    static readonly HashSet<string> Wanted = ["_p0", "_n0", "_t0", "_u0", "_u1", "_c0", "_w0", "_w1", "_i0", "_i1", "bake"];

    public static byte[] Vertices(ShapeRecord shape, Matrix4x4 rigid, Func<BlobKey, byte[]> blob)
    {
        int count = checked((int)shape.VertexCount);
        byte[][] streams = [.. shape.Streams.Select(s => blob(s.Data))];
        Dictionary<string, Vector4[]> attributes = [];
        foreach (AttribRecord attribute in shape.Attribs)
        {
            if (Wanted.Contains(attribute.Name)) attributes[attribute.Name] = Read(shape, attribute, streams, count);
        }

        Vector4[]? Get(string name) => attributes.GetValueOrDefault(name);
        Vector4[]? positions = Get("_p0"), normals = Get("_n0"), tangents = Get("_t0"), uv0 = Get("_u0"), uv1 = Get("_u1"), colors = Get("_c0"), bake = Get("bake");
        Vector4[]? weight0 = Get("_w0"), weight1 = Get("_w1"), index0 = Get("_i0"), index1 = Get("_i1");
        bool rigidBake = shape.SkinCount == 0;
        Matrix4x4 m = rigidBake ? rigid : Matrix4x4.Identity;

        byte[] output = new byte[count * KingVertexLayout.Stride];
        using MemoryStream stream = new(output);
        using BinaryWriter w = new(stream);
        for (int i = 0; i < count; i++)
        {
            Vector3 p = Vector3.Transform(positions is null ? Vector3.Zero : new Vector3(positions[i].X, positions[i].Y, positions[i].Z), m);
            w.Write(p.X); w.Write(p.Y); w.Write(p.Z); w.Write(1f);

            Vector3 n = Vector3.Normalize(Vector3.TransformNormal(normals is null ? Vector3.UnitY : new Vector3(normals[i].X, normals[i].Y, normals[i].Z), m));
            w.Write(n.X); w.Write(n.Y); w.Write(n.Z); w.Write(0f);

            Vector3 t = Vector3.Normalize(Vector3.TransformNormal(tangents is null ? Vector3.UnitX : new Vector3(tangents[i].X, tangents[i].Y, tangents[i].Z), m));
            w.Write(t.X); w.Write(t.Y); w.Write(t.Z); w.Write(tangents is null ? 1f : tangents[i].W);

            // The lightmap coordinates a static shape's programs scale into its atlas rectangle; zero where the shape has none.
            w.Write(bake is null ? 0f : bake[i].X); w.Write(bake is null ? 0f : bake[i].Y); w.Write(0f); w.Write(0f);

            // Full weight on slot 0 where the shape has no weights: skin count 1's programs read no weight at all.
            Vector4 w0 = weight0 is null ? new Vector4(1, 0, 0, 0) : weight0[i], w1 = weight1 is null ? Vector4.Zero : weight1[i];
            w.Write(w0.X); w.Write(w0.Y); w.Write(w0.Z); w.Write(w0.W);
            w.Write(w1.X); w.Write(w1.Y); w.Write(w1.Z); w.Write(w1.W);

            // Palette slots, copied as stored for skin count 1 and up; padding for a baked rigid shape, whose programs have no palette.
            WriteInts(w, rigidBake ? null : index0, i);
            WriteInts(w, rigidBake ? null : index1, i);

            float u = uv0 is null ? 0f : uv0[i].X, v = uv0 is null ? 0f : uv0[i].Y;
            w.Write(u); w.Write(v);
            w.Write(uv1 is null ? u : uv1[i].X); w.Write(uv1 is null ? v : uv1[i].Y);
            w.Write(u); w.Write(v);
            w.Write(u); w.Write(v);

            Vector4 c = colors is null ? Vector4.One : colors[i];
            w.Write(c.X); w.Write(c.Y); w.Write(c.Z); w.Write(c.W);
            w.Write(1f); w.Write(1f); w.Write(1f); w.Write(1f);
        }

        return output;
    }

    static void WriteInts(BinaryWriter w, Vector4[]? values, int i)
    {
        Vector4 v = values is null ? Vector4.Zero : values[i];
        w.Write((int)v.X); w.Write((int)v.Y); w.Write((int)v.Z); w.Write((int)v.W);
    }

    static Vector4[] Read(ShapeRecord shape, AttribRecord attribute, byte[][] streams, int count)
    {
        VertexFormat format = VertexFormat.FromWord(attribute.Format);
        if (attribute.Stream >= streams.Length) throw new BfShaderFormatException($"Attribute {attribute.Name} uses stream {attribute.Stream}");
        int stride = shape.Streams[attribute.Stream].Stride;
        byte[] data = streams[attribute.Stream];
        // A format may be wider than the room its stream leaves (a hair's second weight pair is three bytes in a six-byte stride but declared as four): like the GPU, the bytes of
        // the next vertex are read, and past the end of the stream they read as zero.
        if (attribute.Offset >= stride || (long)stride * count > data.Length) throw new BfShaderFormatException($"Attribute {attribute.Name} (format 0x{attribute.Format:x}, {format.ByteSize} bytes at {attribute.Offset}) does not fit its stream of stride {stride} and {data.Length} bytes for {count} vertices");
        Vector4[] values = new Vector4[count];
        Span<float> floats = stackalloc float[4];
        byte[] edge = new byte[16];
        for (int v = 0; v < count; v++)
        {
            int at = v * stride + attribute.Offset;
            ReadOnlySpan<byte> source = data.AsSpan(at);
            if (source.Length < format.ByteSize)
            {
                Array.Clear(edge);
                source.CopyTo(edge);
                source = edge;
            }

            VertexDecoder.Decode(format, source, floats);
            values[v] = new Vector4(floats[0], floats[1], floats[2], floats[3]);
        }

        return values;
    }

    /// <summary>Every level of detail as 32-bit indices, the finest first and the others re-based on the first level's first vertex, and where each starts as (first index, count).</summary>
    public static (byte[] Indices, List<int[]> Lods) Indices(ShapeRecord shape, Func<BlobKey, byte[]> blob)
    {
        if (shape.Meshes.Count == 0) throw new BfShaderFormatException("A shape has no levels of detail");
        List<int[]> lods = [];
        using MemoryStream stream = new();
        using BinaryWriter w = new(stream);
        long first = 0;
        for (int level = 0; level < shape.Meshes.Count; level++)
        {
            MeshRecord mesh = shape.Meshes[level];
            if (mesh.Primitive != Triangles) throw new BfShaderUnsupportedException($"Primitive type {mesh.Primitive} is not drawn");
            long rebase = (long)mesh.FirstVertex - shape.Meshes[0].FirstVertex;
            byte[] bytes = blob(mesh.Indices);
            int size = mesh.IndexFormat switch { 0 => 1, 1 => 2, 2 => 4, _ => throw new BfShaderUnsupportedException($"Index format {mesh.IndexFormat} is not read") };
            if ((long)mesh.IndexCount * size > bytes.Length) throw new BfShaderFormatException("A mesh has fewer index bytes than its count");
            for (int i = 0; i < mesh.IndexCount; i++)
            {
                uint index = size switch { 1 => bytes[i], 2 => BitConverter.ToUInt16(bytes, i * 2), _ => BitConverter.ToUInt32(bytes, i * 4) };
                w.Write((uint)(level == 0 ? index : index + rebase));
            }

            lods.Add([(int)first, (int)mesh.IndexCount]);
            first += mesh.IndexCount;
        }

        return (stream.ToArray(), lods);
    }
}
