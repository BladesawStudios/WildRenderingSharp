using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// The one draw call every shape variant (G-buffer, z-only, forward) needs - bind its program, its own material UBO at binding 8,
/// its sampler set, and issue the indexed draw.
/// </summary>
public static class ShapeDrawing
{
    public static unsafe void Draw(GL gl, uint materialBinding, uint program, uint vao, MaterialBlock material,
        IReadOnlyList<ShapeSampler> samplers, int indexCount,
        IReadOnlyDictionary<string, LoadedTexture>? overrides = null)
    {
        if (vao == 0 || program == 0)
            return;

        InvalidateStateCache();
        gl.UseProgram(program);
        gl.BindBufferBase(BufferTargetARB.UniformBuffer, materialBinding, material.Handle);
        foreach (var (unit, key, texture) in samplers)
        {
            var bound = texture;
            if (overrides is not null && overrides.TryGetValue(key, out var replacement))
                bound = replacement;
            gl.ActiveTexture(TextureUnit.Texture0 + unit);
            gl.BindTexture(bound.Target, bound.Handle);
        }
        gl.ActiveTexture(TextureUnit.Texture0);

        gl.BindVertexArray(vao);
        gl.DrawElements(PrimitiveType.Triangles, (uint)indexCount, DrawElementsType.UnsignedInt, null);
    }

    public static unsafe void DrawInstanced(GL gl, uint materialBinding, uint program, uint vao, LoadedShape shape,
        IReadOnlyList<ShapeSampler> samplers, InstanceBatch batch, IReadOnlyList<(int First, int Count, int Lod)>? runs = null)
    {
        if (vao == 0 || program == 0)
            return;

        bool multi = InstancingContract.BaseInstance;
        if (!_caching || _program != program)
        {
            gl.UseProgram(program);
            _program = program;
            _locations = InstancedLocations(gl, program);
            _uniformBatch = null;
            // The first-instance uniform is zero for a multi-draw - gl_BaseInstance carries it - and
            // a program keeps its uniforms, so once per program is enough.
            if (multi)
                gl.Uniform1(_locations.First, 0);
        }
        var locations = _locations;
        if (!_caching || !ReferenceEquals(_uniformBatch, batch))
        {
            gl.Uniform1(locations.Stride, batch.Stride);
            gl.Uniform1(locations.PaletteVec4s, batch.PaletteVec4s);
            gl.Uniform1(locations.PaletteRepeat, batch.PaletteRepeats ? 1 : 0);
            _uniformBatch = batch;
        }

        if (!_caching || _material != shape.MaterialBlock.Handle)
        {
            gl.BindBufferBase(BufferTargetARB.UniformBuffer, materialBinding, shape.MaterialBlock.Handle);
            _material = shape.MaterialBlock.Handle;
        }

        // Baked lighting (InstanceBatch.SetBake): the per-instance table at binding 0, and each
        // instance's atlas on the shape's bake0 unit - so a run is drawn in pieces wherever the
        // atlas changes, with an unbaked instance taking the material's own bake0 back. The
        // material's own bake0 is bound only where a run needs it, not first and again after.
        int bakeUnit = -1;
        LoadedTexture? ownBake = null;
        int[]? atlasOf = shape.HasBakeRegion ? batch.BakeAtlasOfInstance : null;
        var overrides = shape.SamplerOverrides;
        for (int i = 0; i < samplers.Count; i++)
        {
            var (unit, key, texture) = samplers[i];
            if (atlasOf is not null && key == "bake0")
            {
                bakeUnit = unit;
                ownBake = texture;
                continue;
            }
            var bound = texture;
            if (overrides is not null && overrides.TryGetValue(key, out var replacement))
                bound = replacement;
            BindTexture(gl, unit, bound.Target, bound.Handle);
        }
        // Bound for every shape of a baked batch: instances still send their shader to this table even where the shape keeps its own bake0.
        if (batch.BakeAtlasOfInstance is not null && (!_caching || _bakeTable != batch.BakeTable))
        {
            gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, BakeTableBinding, batch.BakeTable);
            _bakeTable = batch.BakeTable;
        }

        if (!_caching || _vao != vao)
        {
            gl.BindVertexArray(vao);
            _vao = vao;
        }

        // Every visible run, coalesced where neighbouring runs share a level of detail, goes out as one multi-draw per atlas, each run a command whose base instance says where its instances start.
        // One call per run was tens of thousands of draws a frame for a map, with the card idle between them.
        Commands.Clear();
        int pendingAtlas = int.MinValue;
        var list = runs ?? batch.Visible;
        int openFirst = 0, openCount = 0, openLod = -1;
        for (int r = 0; r < list.Count; r++)
        {
            // Runs merged where one ends exactly where the next begins at the same level of detail.
            var (f, c, l) = list[r];
            if (c <= 0)
                continue;
            if (openCount > 0 && openFirst + openCount == f && openLod == l)
            {
                openCount += c;
                continue;
            }
            if (openCount > 0)
                EmitRun(openFirst, openCount, openLod);
            (openFirst, openCount, openLod) = (f, c, l);
        }
        if (openCount > 0)
            EmitRun(openFirst, openCount, openLod);
        Flush();

        void EmitRun(int first, int count, int lod)
        {
            var (firstIndex, indexCount) = shape.Lod(lod);
            if (indexCount <= 0)
                return;
            if (bakeUnit < 0)
            {
                Emit(first, count, firstIndex, indexCount, int.MinValue);
                return;
            }
            int end = Math.Min(first + count, atlasOf!.Length);
            for (int start = first; start < end;)
            {
                int atlas = atlasOf[start];
                int stop = start + 1;
                while (stop < end && atlasOf[stop] == atlas)
                    stop++;
                Emit(start, stop - start, firstIndex, indexCount, atlas);
                start = stop;
            }
        }

        void Emit(int at, int n, int firstIndex, int indexCount, int atlas)
        {
            _triangles += (long)indexCount / 3 * n;
            _instances += n;
            if (atlas != pendingAtlas)
            {
                Flush();
                pendingAtlas = atlas;
                if (atlas != int.MinValue)
                {
                    var bound = atlas >= 0 ? batch.BakeAtlases[atlas] : ownBake!;
                    BindTexture(gl, bakeUnit, bound.Target, bound.Handle);
                }
            }
            if (!multi)
            {
                gl.Uniform1(locations.First, at);
                gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)indexCount, DrawElementsType.UnsignedInt,
                    (void*)((nint)firstIndex * sizeof(uint)), (uint)n);
                return;
            }
            Commands.Add(new DrawCommand((uint)indexCount, (uint)n, (uint)firstIndex, 0, (uint)at));
        }

        void Flush()
        {
            if (Commands.Count == 0)
                return;
            nint offset = StreamFor(gl).Upload(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Commands));
            gl.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt,
                (void*)offset, (uint)Commands.Count, (uint)sizeof(DrawCommand));
            Commands.Clear();
        }

        // Outside the cache, leave the material's own bake0 where every other draw expects it.
        if (bakeUnit >= 0 && !_caching)
            BindTexture(gl, bakeUnit, ownBake!.Target, ownBake.Handle);
        if (!_caching)
            gl.ActiveTexture(TextureUnit.Texture0);
    }

    static IndirectStream? _stream;

    static IndirectStream StreamFor(GL gl)
    {
        if (_stream is not { } stream || !ReferenceEquals(stream.Gl, gl))
            _stream = stream = IndirectStream.For(gl);
        return stream;
    }
    static Locations _locations;
    static uint _bakeTable;

    public static unsafe bool MultiDrawRuns(GL gl, LoadedShape shape, IReadOnlyList<(int First, int Count, int Lod)> runs)
    {
        if (!InstancingContract.BaseInstance)
            return false;
        Commands.Clear();
        for (int r = 0; r < runs.Count; r++)
        {
            var (first, count, lod) = runs[r];
            var (firstIndex, indexCount) = shape.Lod(lod);
            if (count > 0 && indexCount > 0)
                Commands.Add(new DrawCommand((uint)indexCount, (uint)count, (uint)firstIndex, 0, (uint)first));
        }
        if (Commands.Count > 0)
        {
            nint offset = StreamFor(gl).Upload(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Commands));
            gl.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt,
                (void*)offset, (uint)Commands.Count, (uint)sizeof(DrawCommand));
            Commands.Clear();
        }
        return true;
    }

    // ---- state cache: redundant binds skipped between BeginStateCache and EndStateCache ----

    static bool _caching;
    static uint _program, _material, _vao;
    static InstanceBatch? _uniformBatch;
    static readonly uint[] _textures = new uint[96];
    static int _activeUnit = -1;

    public static void BeginStateCache()
    {
        InvalidateStateCache();
        _caching = true;
    }

    public static void EndStateCache(GL? gl = null)
    {
        _caching = false;
        InvalidateStateCache();
    }

    static void InvalidateStateCache()
    {
        _program = _material = _vao = _bakeTable = 0;
        _uniformBatch = null;
        Array.Clear(_textures);
        _activeUnit = -1;
    }

    static void BindTexture(GL gl, int unit, TextureTarget target, uint handle)
    {
        if (_caching && (uint)unit < (uint)_textures.Length && _textures[unit] == handle)
            return;
        if (!_caching || _activeUnit != unit)
        {
            gl.ActiveTexture(TextureUnit.Texture0 + unit);
            _activeUnit = unit;
        }
        gl.BindTexture(target, handle);
        if (_caching && (uint)unit < (uint)_textures.Length)
            _textures[unit] = handle;
    }

    public const uint BakeTableBinding = 0;

    static long _triangles, _instances;

    public static (long Triangles, long Instances) TakeCounts()
    {
        var counts = (_triangles, _instances);
        _triangles = _instances = 0;
        return counts;
    }

    /// <summary>One command of a multi-draw: GL's DrawElementsIndirectCommand.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    readonly record struct DrawCommand(uint Count, uint InstanceCount, uint FirstIndex, int BaseVertex, uint BaseInstance);

    static readonly List<DrawCommand> Commands = [];

    /// <summary>A ring the multi-draw commands are written into, bound as the draw-indirect buffer.</summary>
    sealed unsafe class IndirectStream
    {
        const int Capacity = 8 << 20, Chunks = 8, ChunkSize = Capacity / Chunks;
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GL, IndirectStream> Streams = new();
        readonly GL _gl;
        readonly uint _buffer;
        readonly byte* _mapped;

        public GL Gl => _gl;
        readonly nint[] _fences = new nint[Chunks];
        int _offset, _chunk;

        IndirectStream(GL gl)
        {
            _gl = gl;
            _buffer = gl.GenBuffer();
            gl.BindBuffer(BufferTargetARB.DrawIndirectBuffer, _buffer);
            if (gl.IsExtensionPresent("GL_ARB_buffer_storage"))
            {
                const uint flags = (uint)(GLEnum.MapWriteBit | GLEnum.MapPersistentBit | GLEnum.MapCoherentBit);
                gl.BufferStorage(BufferStorageTarget.DrawIndirectBuffer, (nuint)Capacity, null, (BufferStorageMask)flags);
                _mapped = (byte*)gl.MapBufferRange(BufferTargetARB.DrawIndirectBuffer, 0, (nuint)Capacity, (MapBufferAccessMask)flags);
            }
            if (_mapped is null)
                gl.BufferData(BufferTargetARB.DrawIndirectBuffer, (nuint)Capacity, null, BufferUsageARB.StreamDraw);
        }

        public static IndirectStream For(GL gl) => Streams.GetValue(gl, g => new IndirectStream(g));

        public nint Upload(ReadOnlySpan<DrawCommand> commands)
        {
            int bytes = commands.Length * sizeof(DrawCommand);
            _gl.BindBuffer(BufferTargetARB.DrawIndirectBuffer, _buffer);
            if (_mapped is null)
            {
                if (_offset + bytes > Capacity)
                {
                    _gl.BufferData(BufferTargetARB.DrawIndirectBuffer, (nuint)Capacity, null, BufferUsageARB.StreamDraw);
                    _offset = 0;
                }
                fixed (DrawCommand* p = commands)
                    _gl.BufferSubData(BufferTargetARB.DrawIndirectBuffer, _offset, (nuint)bytes, p);
            }
            else
            {
                if (bytes > ChunkSize)
                    throw new InvalidOperationException($"{commands.Length} indirect commands in one call");
                if (_offset + bytes > (_chunk + 1) * ChunkSize)
                {
                    // Done with this chunk: fence it, and move on to the next once the card is past
                    // its last use.
                    _fences[_chunk] = _gl.FenceSync(SyncCondition.SyncGpuCommandsComplete, SyncBehaviorFlags.None);
                    _chunk = (_chunk + 1) % Chunks;
                    if (_fences[_chunk] != 0)
                    {
                        _gl.ClientWaitSync(_fences[_chunk], SyncObjectMask.Bit, 1_000_000_000);
                        _gl.DeleteSync(_fences[_chunk]);
                        _fences[_chunk] = 0;
                    }
                    _offset = _chunk * ChunkSize;
                }
                commands.CopyTo(new Span<DrawCommand>(_mapped + _offset, commands.Length));
            }
            nint at = _offset;
            _offset += (bytes + 15) & ~15;
            return at;
        }
    }

    internal static void ForgetPrograms(IEnumerable<uint> programs)
    {
        foreach (uint program in programs)
            LocationCache.Remove(program);
    }

    readonly record struct Locations(int First, int Stride, int PaletteVec4s, int PaletteRepeat);

    static readonly Dictionary<uint, Locations> LocationCache = [];

    // The instancing uniforms' locations, looked up once per program. Keyed by program name, which GL reuses after a delete -
    // so ShaderProgramCache calls ForgetPrograms when it deletes the programs these belong to.
    static Locations InstancedLocations(GL gl, uint program)
    {
        if (!LocationCache.TryGetValue(program, out var l))
        {
            l = new Locations(
                gl.GetUniformLocation(program, InstancingContract.FirstInstanceUniform),
                gl.GetUniformLocation(program, InstancingContract.StrideUniform),
                gl.GetUniformLocation(program, InstancingContract.PaletteVec4sUniform),
                gl.GetUniformLocation(program, InstancingContract.PaletteRepeatUniform));
            LocationCache[program] = l;
        }
        return l;
    }
}
