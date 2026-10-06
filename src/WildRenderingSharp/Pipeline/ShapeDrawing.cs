using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Pipeline;

/// <summary>The one draw call every shape variant (G-buffer, z-only, forward) needs - bind its program, its own material UBO at binding 8, its sampler set, and issue the indexed draw.</summary>
public static class ShapeDrawing
{
    /// <param name="overrides">
    /// Sampler key -> a texture to bind instead of the one <paramref name="samplers"/> resolved for
    /// that key, or null for none. This is how a texture pattern anim takes effect: it re-points a
    /// sampler at a different texture without touching the program, the VAO, or the material UBO.
    /// </param>
    public static unsafe void Draw(GL gl, uint program, uint vao, uint materialUboBuffer,
        IReadOnlyList<ShapeSampler> samplers, int indexCount,
        IReadOnlyDictionary<string, LoadedTexture>? overrides = null)
    {
        if (vao == 0 || program == 0)
            return;

        gl.UseProgram(program);
        gl.BindBufferBase(BufferTargetARB.UniformBuffer, 8, materialUboBuffer);
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

    /// <summary>
    /// <see cref="Draw"/> for every visible run of a batch: the same program state, then one
    /// instanced call per run, each at that run's level of detail. The instance buffer is already
    /// bound (<see cref="ActorDrawGroup.BindUbos"/>).
    /// </summary>
    public static unsafe void DrawInstanced(GL gl, uint program, uint vao, LoadedShape shape,
        IReadOnlyList<ShapeSampler> samplers, InstanceBatch batch, IReadOnlyList<(int First, int Count, int Lod)>? runs = null)
    {
        if (vao == 0 || program == 0)
            return;

        gl.UseProgram(program);
        var locations = InstancedLocations(gl, program);
        gl.Uniform1(locations.Stride, batch.Stride);
        gl.Uniform1(locations.PaletteVec4s, batch.PaletteVec4s);
        gl.Uniform1(locations.PaletteRepeat, batch.PaletteRepeats ? 1 : 0);

        gl.BindBufferBase(BufferTargetARB.UniformBuffer, 8, shape.MaterialUboBuffer);
        var overrides = shape.SamplerOverrides;
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

        // Baked lighting (InstanceBatch.SetBake): the per-instance table at binding 0, and each
        // instance's atlas on the shape's bake0 unit - so a run is drawn in pieces wherever the
        // atlas changes, with an unbaked instance taking the material's own bake0 back.
        int bakeUnit = -1;
        LoadedTexture? ownBake = null;
        int[]? atlasOf = batch.BakeAtlasOfInstance;
        if (atlasOf is not null)
        {
            foreach (var (unit, key, texture) in samplers)
                if (key == "bake0")
                {
                    bakeUnit = unit;
                    ownBake = texture;
                }
            gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, BakeTableBinding, batch.BakeTable);
        }

        // Every visible run - coalesced where Prism-style cells leave neighbouring runs at one level
        // of detail - goes out as one multi-draw per atlas, each run a command whose base instance
        // says where its instances start. One call per run used to be tens of thousands of draws a
        // frame for a map, and the card sat idle between them.
        bool multi = InstancedShaderPatch.BaseInstance;
        if (multi)
            gl.Uniform1(locations.First, 0);
        Commands.Clear();
        int pendingAtlas = int.MinValue;

        foreach (var (first, count, lod) in Coalesce(runs ?? batch.Visible))
        {
            var (firstIndex, indexCount) = shape.Lod(lod);
            if (indexCount <= 0)
                continue;
            if (bakeUnit < 0 || atlasOf is null)
            {
                Emit(first, count, firstIndex, indexCount, int.MinValue);
                continue;
            }
            int end = Math.Min(first + count, atlasOf.Length);
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
        Flush();

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
                    gl.ActiveTexture(TextureUnit.Texture0 + bakeUnit);
                    gl.BindTexture(bound.Target, bound.Handle);
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
            nint offset = IndirectStream.For(gl).Upload(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Commands));
            gl.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt,
                (void*)offset, (uint)Commands.Count, (uint)sizeof(DrawCommand));
            Commands.Clear();
        }

        if (bakeUnit >= 0)
        {
            gl.ActiveTexture(TextureUnit.Texture0 + bakeUnit);
            gl.BindTexture(ownBake!.Target, ownBake.Handle);
            gl.ActiveTexture(TextureUnit.Texture0);
        }
    }

    /// <summary>Where the static-object shaders read their per-instance bake table from (<c>vp_s0</c>).</summary>
    public const uint BakeTableBinding = 0;

    /// <summary>Triangles and instances submitted by instanced draws since the counters were last read - see <see cref="TakeCounts"/>.</summary>
    static long _triangles, _instances;

    /// <summary>Reads and resets what instanced draws have submitted.</summary>
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

    /// <summary>Runs merged where one ends exactly where the next begins at the same level of detail.</summary>
    static IEnumerable<(int First, int Count, int Lod)> Coalesce(IEnumerable<(int First, int Count, int Lod)> runs)
    {
        (int First, int Count, int Lod)? open = null;
        foreach (var run in runs)
        {
            if (run.Count <= 0)
                continue;
            if (open is { } o && o.First + o.Count == run.First && o.Lod == run.Lod)
            {
                open = (o.First, o.Count + run.Count, o.Lod);
                continue;
            }
            if (open is { } done)
                yield return done;
            open = run;
        }
        if (open is { } last)
            yield return last;
    }

    /// <summary>
    /// A ring the multi-draw commands are written into, bound as the draw-indirect buffer; it is
    /// orphaned when it wraps, so a write never waits on the card still reading earlier commands.
    /// </summary>
    sealed unsafe class IndirectStream
    {
        const int Capacity = 4 << 20;
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GL, IndirectStream> Streams = new();
        readonly GL _gl;
        readonly uint _buffer;
        int _offset;

        IndirectStream(GL gl)
        {
            _gl = gl;
            _buffer = gl.GenBuffer();
            gl.BindBuffer(BufferTargetARB.DrawIndirectBuffer, _buffer);
            gl.BufferData(BufferTargetARB.DrawIndirectBuffer, (nuint)Capacity, null, BufferUsageARB.StreamDraw);
        }

        public static IndirectStream For(GL gl) => Streams.GetValue(gl, g => new IndirectStream(g));

        public nint Upload(ReadOnlySpan<DrawCommand> commands)
        {
            int bytes = commands.Length * sizeof(DrawCommand);
            _gl.BindBuffer(BufferTargetARB.DrawIndirectBuffer, _buffer);
            if (_offset + bytes > Capacity)
            {
                _gl.BufferData(BufferTargetARB.DrawIndirectBuffer, (nuint)Capacity, null, BufferUsageARB.StreamDraw);
                _offset = 0;
            }
            fixed (DrawCommand* p = commands)
                _gl.BufferSubData(BufferTargetARB.DrawIndirectBuffer, _offset, (nuint)bytes, p);
            nint at = _offset;
            _offset += (bytes + 15) & ~15;
            return at;
        }
    }

    /// <summary>Drops every remembered location - the programs they were looked up in are being deleted.</summary>
    internal static void ForgetPrograms(IEnumerable<uint> programs)
    {
        foreach (uint program in programs)
            LocationCache.Remove(program);
    }

    readonly record struct Locations(int First, int Stride, int PaletteVec4s, int PaletteRepeat);

    static readonly Dictionary<uint, Locations> LocationCache = [];

    /// <summary>
    /// The instancing uniforms' locations, looked up once per program. Keyed by program name, which
    /// GL reuses after a delete - so <see cref="ShaderProgramCache"/> calls <see cref="ForgetPrograms"/>
    /// when it deletes the programs these belong to.
    /// </summary>
    static Locations InstancedLocations(GL gl, uint program)
    {
        if (!LocationCache.TryGetValue(program, out var l))
        {
            l = new Locations(
                gl.GetUniformLocation(program, InstancedShaderPatch.FirstInstanceUniform),
                gl.GetUniformLocation(program, InstancedShaderPatch.StrideUniform),
                gl.GetUniformLocation(program, InstancedShaderPatch.PaletteVec4sUniform),
                gl.GetUniformLocation(program, InstancedShaderPatch.PaletteRepeatUniform));
            LocationCache[program] = l;
        }
        return l;
    }
}
