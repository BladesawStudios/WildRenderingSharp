using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Assets.Loading;
using WildRenderingSharp.Assets.Textures;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Pipeline.Merged;

/// <summary>Draws the shapes of instanced batches from shared buffers, so a pass's draws go out as one multi-draw for each program and chunk of geometry, not one for each shape.</summary>
/// <remarks>
/// A shape is added with the runs of its batch that are visible and is drawn at <see cref="Flush"/>. A shape this cannot draw, whose program did not patch or
/// whose textures change, is refused and drawn the ordinary way.
/// </remarks>
internal sealed unsafe partial class MergedDrawing : IDisposable
{
    // Held while anything of the merged buffers is changed, which models loading on another thread do.
    readonly object _sync = new();
    readonly GL _gl;
    readonly ShaderProgramCache _programs;
    readonly IndirectStream _stream;
    readonly DefaultBake _defaultBake;
    readonly Action _invalidate;
    readonly MergedDrawList _list = new();
    readonly List<DrawRecord> _records = [];
    readonly Dictionary<(int Chunk, uint Program), uint> _arrays = [];
    readonly Dictionary<uint, int> _baseLocations = [];
    readonly VertexArrays _vertexArrays;
    bool? _supported;
    long _triangles, _instances;

    BindlessHandles? _handles;
    GeometryArena? _geometry;
    MergedMaterials? _materials;
    MergedTextures? _textures;
    MergedInstances? _batches;
    LoadedTexture? _white;

    public MergedDrawing(GL gl, ShaderProgramCache programs, IndirectStream stream, DefaultBake defaultBake, Action invalidate)
    {
        (_gl, _programs, _stream, _defaultBake, _invalidate) = (gl, programs, stream, defaultBake, invalidate);
        _vertexArrays = new VertexArrays(gl);
    }

    // Which handles of this renderer's are resident, shared with the textures so that deleting one lets go of its handles first.
    public TextureResidency Residency { get; set; } = new();

    // Whether shapes are drawn this way: the viewer's choice, and the context able to.
    public bool Enabled { get; set; }

    // Whether the pass drawing now takes shapes this way, which it says by starting to collect them and ends with a flush.
    public bool Collecting { get; private set; }

    public bool Usable => Enabled && Collecting && (_supported ??= MergedSupport.Available(_gl));

    public void BeginCollecting() => Collecting = true;

    public (long Triangles, long Instances) TakeCounts()
    {
        var counts = (_triangles, _instances);
        _triangles = _instances = 0;
        return counts;
    }

    // A shape that samples baked lighting belongs to the map's own static objects: overlapping surfaces that the order the ordinary draw sorts them into decides
    // between, and baked lighting that has looked different drawn from the merged buffers. Those stay with the ordinary draw, in every pass, so that a shape is never
    // drawn by one of the two in its prepass and the other in its main draw. It is a property of the shape, so it holds before a batch's baked lighting has arrived too.
    // So does a _Far stand-in, whose lighting looked wrong drawn from the merged buffers in the map where it looks right drawn the ordinary way. The cause is not found; these
    // are a few hundred cheap models, and drawing them as they were drawn is the safe side of not knowing.
    internal static bool IsLeftToTheOrdinaryDraw(LoadedShape shape) => shape.IsFarStandIn || shape.GBufferSamplers.Any(s => s.Key == "bake0");

    // Queues the visible runs of the shape's batch; false when the shape is left to be drawn the ordinary way.
    public bool TryAdd(ShapeProgram which, LoadedShape shape, InstanceBatch batch, List<(int First, int Count, int Lod)> runs)
    {
        if (!Usable || shape.SamplerOverrides is not null || shape.Buffers is null || which == ShapeProgram.Forward || IsLeftToTheOrdinaryDraw(shape))
            return false;
        var merged = MergedOf(shape);
        uint program = merged.Excluded ? 0 : ProgramOf(shape, merged, which);
        if (program == 0)
            return false;

        var records = batch.Merged ?? AddBatch(batch)!;
        int textures = TexturesOf(shape, merged, which);
        int flags = shape.HasBakeRegion && batch.BakeAtlasOfInstance is not null ? MergedLayout.UsesInstanceAtlas : 0;
        var record = new DrawRecord(records.InstanceBase, records.Stride, batch.PaletteVec4s, batch.PaletteRepeats ? 1 : 0, merged.MaterialBase, textures, flags, 0);
        AddRuns(shape, merged, program, record, runs);
        return true;
    }

    // Queues the stamp of a shape's silhouette in the pass-ID mask, whose program is its own and which needs of the shape only its skin count and the pass it belongs to.
    public bool TryAddPassId(uint program, LoadedShape shape, InstanceBatch batch, List<(int First, int Count, int Lod)> runs, int passValue)
    {
        if (!Usable || shape.Buffers is null || program == 0 || IsLeftToTheOrdinaryDraw(shape))
            return false;
        var merged = MergedOf(shape);
        var records = batch.Merged ?? AddBatch(batch)!;
        AddRuns(shape, merged, program, new DrawRecord(records.InstanceBase, records.Stride, batch.PaletteVec4s, batch.PaletteRepeats ? 1 : 0, passValue, 0, 0, shape.VertexSkinCount), runs);
        return true;
    }

    // Draws everything queued, and leaves the state the ordinary draws expect to find changed.
    public void Flush()
    {
        Collecting = false;
        if (_list.IsEmpty)
            return;
        lock (_sync)
        {
            _textures!.Refresh();
            UploadRecords();
            Bind(0, _batches!.BakeTable.Handle);
            Bind(MergedLayout.MaterialBinding, _materials!.Buffer.Handle);
            Bind(MergedLayout.HandleBinding, _textures.Buffer.Handle);
            Bind(InstancingContract.InstanceBinding, _batches.Instances.Handle);
        }

        foreach (var bucket in _list.Buckets)
        {
            _gl.UseProgram(bucket.Program);
            _gl.Uniform1(BaseLocation(bucket.Program), (int)_baseOf[bucket]);
            _gl.BindVertexArray(ArrayFor(bucket.Chunk, bucket.Program));
            _stream.Draw(bucket.Commands);
        }
        _list.Clear();
        _gl.BindVertexArray(0);
        _invalidate();
    }

    public void Release(LoadedShape shape, MergedShape merged)
    {
        lock (_sync)
        {
            _geometry!.Release(merged.Geometry);
            _materials!.Remove(shape.MaterialBlock, merged.MaterialBase);
            if (merged.GBufferTextures >= 0) _textures!.Remove(merged.GBufferTextures);
            if (merged.ZOnlyTextures >= 0) _textures!.Remove(merged.ZOnlyTextures);
            shape.Merged = null;
        }
    }

    public void Release(InstanceBatch batch)
    {
        lock (_sync)
            _batches?.Remove(batch);
    }

    public void Dispose()
    {
        _geometry?.Dispose();
        _materials?.Dispose();
        _textures?.Dispose();
        _batches?.Dispose();
        foreach (uint array in _arrays.Values)
            _gl.DeleteVertexArray(array);
    }
}
