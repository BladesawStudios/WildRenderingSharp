using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Assets.Textures;
using WildRenderingSharp.Pipeline.Drawing;

namespace WildRenderingSharp.Pipeline.Merged;

// The half of merged drawing that gets things into the buffers and the draws out of them.
internal sealed unsafe partial class MergedDrawing
{
    readonly Dictionary<MergedDrawList.Bucket, int> _baseOf = [];

    // Gets a model's shapes into the buffers and links their programs, which a loader does on its own thread while the model is made, so that drawing it
    // finds all of that done and never waits for a link.
    public void Prepare(LoadedModel model)
    {
        if (!MergedSupport.Available(_gl))
            return;
        var giveUp = new List<LoadedShape>();
        foreach (var shape in model.Shapes)
        {
            if (shape.Buffers is null || IsLeftToTheOrdinaryDraw(shape))
                continue;
            var merged = MergedOf(shape);
            if (merged.Excluded)
                continue;
            uint gBuffer = ProgramOf(shape, merged, ShapeProgram.GBuffer);
            uint zOnly = ProgramOf(shape, merged, ShapeProgram.ZOnly);
            TexturesOf(shape, merged, ShapeProgram.GBuffer);
            if (shape.ZOnlyShaderName.Length > 0)
                TexturesOf(shape, merged, ShapeProgram.ZOnly);
            // Drawn from the copy in every pass it takes part in, so its own buffers would only be a second copy of the same vertices.
            if (gBuffer != 0 && (shape.ZOnlyShaderName.Length == 0 || zOnly != 0) && !shape.Blend)
                giveUp.Add(shape);
        }

        void Demote()
        {
            foreach (var shape in giveUp)
                shape.Demote(_gl);
        }
        // The vertex arrays are made on the renderer's thread for a model loaded on another, and are what the buffers are given up after.
        if (model.PendingVertexArrays is null)
            Demote();
        else
            model.AfterVertexArrays = Demote;
    }

    // Makes a demoted shape's own buffers and vertex arrays again from its place in the merged geometry.
    public void Restore(LoadedShape shape, MergedShape merged)
    {
        var slice = merged.Geometry;
        var chunk = slice.Chunk;
        long vertexBytes = (long)slice.VertexCount * chunk.Stride, indexBytes = (long)slice.IndexCount * sizeof(uint);
        uint vbo = Remake(chunk.Vbo, BufferTargetARB.ArrayBuffer, (long)slice.BaseVertex * chunk.Stride, vertexBytes);
        uint ibo = Remake(chunk.Ibo, BufferTargetARB.ElementArrayBuffer, (long)slice.FirstIndex * sizeof(uint), indexBytes);
        var buffers = shape.Buffers with { Vbo = vbo, Ibo = ibo };
        var arrays = new Assets.Loading.VertexArrays(_gl);
        shape.Rebuilt(vbo, ibo, arrays.ForProgram(shape.GBufferProgram, buffers),
            shape.ZOnlyProgram != 0 ? arrays.ForProgram(shape.ZOnlyProgram, buffers) : 0,
            shape.ForwardProgram != 0 ? arrays.ForProgram(shape.ForwardProgram, buffers) : 0,
            arrays.ForPassId(buffers));
    }

    uint Remake(uint source, BufferTargetARB kind, long at, long bytes)
    {
        uint buffer = _gl.GenBuffer();
        _gl.BindBuffer(kind, buffer);
        _gl.BufferData(kind, (nuint)bytes, null, BufferUsageARB.StaticDraw);
        _gl.BindBuffer(BufferTargetARB.CopyReadBuffer, source);
        _gl.BindBuffer(BufferTargetARB.CopyWriteBuffer, buffer);
        _gl.CopyBufferSubData(CopyBufferSubDataTarget.CopyReadBuffer, CopyBufferSubDataTarget.CopyWriteBuffer, (nint)at, 0, (nuint)bytes);
        return buffer;
    }

    // Gets a batch's instances into the merged buffers, as they are made, so that drawing it finds that done.
    public void Prepare(InstanceBatch batch)
    {
        if (MergedSupport.Available(_gl))
            AddBatch(batch);
    }

    MergedBatch? AddBatch(InstanceBatch batch)
    {
        lock (_sync)
        {
            Ensure();
            return batch.Merged ?? _batches!.Add(batch);
        }
    }

    MergedShape MergedOf(LoadedShape shape)
    {
        if (shape.Merged is MergedShape held)
            return held;
        lock (_sync)
        {
            Ensure();
            return shape.Merged as MergedShape ?? Register(shape);
        }
    }

    // Makes the buffers the first time something is drawn this way.
    void Ensure()
    {
        if (_handles is not null)
            return;
        _handles = new BindlessHandles(_gl);
        _geometry = new GeometryArena(_gl);
        _materials = new MergedMaterials(_gl, _sync);
        _textures = new MergedTextures(_gl, _handles, _defaultBake);
        _batches = new MergedInstances(_gl, _handles, _sync);
        _white = WhiteTexture();
    }

    // Copies the shape into the geometry and material buffers; its textures follow for each program that is asked for.
    MergedShape Register(LoadedShape shape)
    {
        var merged = new MergedShape(this, _geometry!.Add(shape), _materials!.Add(shape.MaterialBlock))
        {
            Excluded = shape.GBufferSamplers.Concat(shape.ZOnlySamplers).Any(s => ExternalTextures.IsArraySampler(s.Key)),
        };
        shape.Merged = merged;
        return merged;
    }

    uint ProgramOf(LoadedShape shape, MergedShape merged, ShapeProgram which)
    {
        if (which == ShapeProgram.ZOnly)
            return merged.ZOnlyProgram ??= shape.ZOnlyShaderName.Length > 0 ? _programs.LoadMerged(shape.ZOnlyShaderName) : 0;
        return merged.GBufferProgram ??= shape.GBufferShaderName.Length > 0 ? _programs.LoadMerged(shape.GBufferShaderName) : 0;
    }

    int TexturesOf(LoadedShape shape, MergedShape merged, ShapeProgram which)
    {
        int held = which == ShapeProgram.ZOnly ? merged.ZOnlyTextures : merged.GBufferTextures;
        if (held >= 0)
            return held;
        lock (_sync)
        {
            if (which == ShapeProgram.ZOnly)
                return merged.ZOnlyTextures = merged.ZOnlyTextures >= 0 ? merged.ZOnlyTextures : _textures!.Add(shape.ZOnlySamplers, _white!);
            return merged.GBufferTextures = merged.GBufferTextures >= 0 ? merged.GBufferTextures : _textures!.Add(shape.GBufferSamplers, _white!);
        }
    }

    // One command for each stretch of consecutive instances at one level of detail, as the ordinary draw merges them.
    void AddRuns(LoadedShape shape, MergedShape merged, uint program, in DrawRecord record, List<(int First, int Count, int Lod)> runs)
    {
        int openFirst = 0, openCount = 0, openLod = -1;
        for (int i = 0; i < runs.Count; i++)
        {
            var (first, count, lod) = runs[i];
            if (count <= 0)
                continue;
            if (openCount > 0 && openFirst + openCount == first && openLod == lod)
            {
                openCount += count;
                continue;
            }
            AddCommand(shape, merged, program, record, openFirst, openCount, openLod);
            (openFirst, openCount, openLod) = (first, count, lod);
        }
        AddCommand(shape, merged, program, record, openFirst, openCount, openLod);
    }

    void AddCommand(LoadedShape shape, MergedShape merged, uint program, in DrawRecord record, int first, int count, int lod)
    {
        if (count <= 0)
            return;
        var (firstIndex, indexCount) = shape.Lod(lod);
        if (indexCount <= 0)
            return;
        var slice = merged.Geometry;
        _triangles += (long)indexCount / 3 * count;
        _instances += count;
        _list.Add(program, slice.Chunk, new DrawCommand((uint)indexCount, (uint)count, (uint)(slice.FirstIndex + firstIndex), slice.BaseVertex, (uint)first), record);
    }

    // The records of every bucket one after another in one buffer, each bucket knowing where its own begin.
    void UploadRecords()
    {
        _records.Clear();
        _baseOf.Clear();
        foreach (var bucket in _list.Buckets)
        {
            _baseOf[bucket] = _records.Count;
            _records.AddRange(bucket.Records);
        }
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_records);
        nint at = _stream.Upload<DrawRecord>(span, RecordAlignment);
        _gl.BindBufferRange(BufferTargetARB.ShaderStorageBuffer, MergedLayout.DrawBinding, _stream.Buffer, at, (nuint)(span.Length * sizeof(DrawRecord)));
    }

    // Storage buffers may be bound from offsets that are multiples of this, which no context needs more than.
    const int RecordAlignment = 256;

    void Bind(uint binding, uint buffer) => _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, binding, buffer);

    int BaseLocation(uint program)
    {
        if (!_baseLocations.TryGetValue(program, out int location))
            _baseLocations[program] = location = _gl.GetUniformLocation(program, MergedLayout.DrawBaseUniform);
        return location;
    }

    // The vertex array that reads the chunk for the program, which is built the first time and kept.
    uint ArrayFor(GeometryChunk chunk, uint program)
    {
        if (!_arrays.TryGetValue((chunk.Id, program), out uint array))
            _arrays[(chunk.Id, program)] = array = _vertexArrays.ForProgram(program, chunk.AsBuffers);
        return array;
    }

    LoadedTexture WhiteTexture()
    {
        uint handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, handle);
        byte* texel = stackalloc byte[] { 255, 255, 255, 255 };
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, texel);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        return new LoadedTexture { Handle = handle, Width = 1, Height = 1, Name = "(merged missing)" };
    }
}
