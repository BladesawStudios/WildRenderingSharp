using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Assets.Loading;
using WildRenderingSharp.Assets.Manifests;

namespace WildRenderingSharp.Pipeline.Merged;

/// <summary>Where a shape's vertices and indices lie in a chunk of the arena: the vertex its first one is, the index its first one is, and how many of each.</summary>
internal sealed record GeometrySlice(GeometryChunk Chunk, int BaseVertex, int FirstIndex, int VertexCount, int IndexCount);

/// <summary>A pair of buffers holding the vertices and indices of many shapes that share a vertex layout, so one vertex array reads them all.</summary>
internal sealed class GeometryChunk(GL gl, int id, string key, ShapeBuffers format, int vertexCapacity, int indexCapacity)
{
    public int Id { get; } = id;

    public string Key { get; } = key;

    public List<VertexLayoutEntry> Layout { get; } = format.Layout;

    public int Stride { get; } = format.Stride;

    public bool ConstantSkin { get; } = format.ConstantSkin;

    public uint Vbo { get; } = GLBufferOf(gl, BufferTargetARB.ArrayBuffer, (long)vertexCapacity * format.Stride);

    public uint Ibo { get; } = GLBufferOf(gl, BufferTargetARB.ElementArrayBuffer, (long)indexCapacity * sizeof(uint));

    long Bytes { get; } = Counted((long)vertexCapacity * format.Stride + (long)indexCapacity * sizeof(uint));

    static long Counted(long bytes)
    {
        Gpu.GpuMemory.Merged(bytes);
        return bytes;
    }

    public RegionAllocator Vertices { get; } = new(vertexCapacity);

    public RegionAllocator Indices { get; } = new(indexCapacity);

    // The buffers as a shape's own are described, which is what a vertex array is built from.
    public ShapeBuffers AsBuffers => new(Layout, Stride, Vbo, Ibo, ConstantSkin, 0, 0);

    public void Dispose()
    {
        gl.DeleteBuffer(Vbo);
        gl.DeleteBuffer(Ibo);
        Gpu.GpuMemory.Merged(-Bytes);
    }

    static unsafe uint GLBufferOf(GL gl, BufferTargetARB target, long bytes)
    {
        uint buffer = gl.GenBuffer();
        gl.BindBuffer(target, buffer);
        gl.BufferData(target, (nuint)bytes, null, BufferUsageARB.StaticDraw);
        return buffer;
    }
}

/// <summary>Copies shapes' vertices and indices into shared chunks, one set of chunks for each vertex layout, so shapes of any model can be drawn by one call.</summary>
internal sealed class GeometryArena(GL gl) : IDisposable
{
    // A chunk is this many bytes of vertices; a shape larger than that gets a chunk of its own.
    const long ChunkVertexBytes = 64L << 20;

    readonly Dictionary<string, List<GeometryChunk>> _chunks = new(StringComparer.Ordinal);
    int _nextId;

    public GeometrySlice Add(LoadedShape shape)
    {
        ShapeBuffers format = shape.Buffers;
        int vertices = format.VertexBytes / format.Stride, indices = format.IndexBytes / sizeof(uint);
        string key = KeyOf(format);
        if (!_chunks.TryGetValue(key, out var list))
            _chunks[key] = list = [];

        foreach (var chunk in list)
            if (Place(chunk, shape, vertices, indices) is { } slice)
                return slice;

        int capacity = (int)Math.Max(ChunkVertexBytes / format.Stride, vertices);
        var made = new GeometryChunk(gl, _nextId++, key, format, capacity, Math.Max(capacity * 3, indices));
        list.Add(made);
        return Place(made, shape, vertices, indices) ?? throw new InvalidOperationException("A new chunk has no room for the shape that made it.");
    }

    public void Release(GeometrySlice slice)
    {
        slice.Chunk.Vertices.Free(slice.BaseVertex, slice.VertexCount);
        slice.Chunk.Indices.Free(slice.FirstIndex, slice.IndexCount);
    }

    public void Dispose()
    {
        foreach (var chunk in _chunks.Values.SelectMany(c => c))
            chunk.Dispose();
        _chunks.Clear();
    }

    GeometrySlice? Place(GeometryChunk chunk, LoadedShape shape, int vertices, int indices)
    {
        int baseVertex = chunk.Vertices.Allocate(vertices);
        if (baseVertex < 0)
            return null;
        int firstIndex = chunk.Indices.Allocate(indices);
        if (firstIndex < 0)
        {
            chunk.Vertices.Free(baseVertex, vertices);
            return null;
        }

        Copy(shape.VertexBuffer, BufferTargetARB.ArrayBuffer, chunk.Vbo, (long)baseVertex * chunk.Stride, (long)vertices * chunk.Stride);
        Copy(shape.IndexBuffer, BufferTargetARB.ElementArrayBuffer, chunk.Ibo, (long)firstIndex * sizeof(uint), (long)indices * sizeof(uint));
        return new GeometrySlice(chunk, baseVertex, firstIndex, vertices, indices);
    }

    // A GPU-side copy: the shape's own buffer is the source, so nothing is read back or kept on the CPU.
    void Copy(uint source, BufferTargetARB kind, uint destination, long at, long bytes)
    {
        gl.BindBuffer(BufferTargetARB.CopyReadBuffer, source);
        gl.BindBuffer(BufferTargetARB.CopyWriteBuffer, destination);
        gl.CopyBufferSubData(CopyBufferSubDataTarget.CopyReadBuffer, CopyBufferSubDataTarget.CopyWriteBuffer, 0, (nint)at, (nuint)bytes);
    }

    static string KeyOf(ShapeBuffers format) =>
        $"{format.Stride}|{format.ConstantSkin}|" + string.Join(';', format.Layout.OrderBy(e => e.Location).Select(e => $"{e.Location}:{e.Offset}:{e.Components}"));
}
