using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Pipeline.Drawing;

// The baked-lighting sampler a batch's shape draws with: its unit, the material's own texture for it, and which atlas each instance uses.
readonly record struct BakeSlot(int Unit, LoadedTexture Own, int[] AtlasOf);

/// <summary>Draws a batch's visible runs of one shape, merging neighbouring runs and splitting them wherever the baked-lighting atlas changes.</summary>
sealed unsafe class InstancedRunWriter(GL gl, IndirectStream stream, DrawBindingCache bound)
{
    const int NoAtlas = int.MinValue;

    readonly List<DrawCommand> _commands = [];
    LoadedShape _shape = null!;
    InstanceBatch _batch = null!;
    BakeSlot? _bake;
    int _firstInstanceLocation;
    int _pendingAtlas;
    long _triangles, _instances;

    public (long Triangles, long Instances) TakeCounts()
    {
        var counts = (_triangles, _instances);
        _triangles = _instances = 0;
        return counts;
    }

    // Every run goes out as one multi-draw per atlas, each run a command whose base instance says where its instances start.
    public void Draw(LoadedShape shape, InstanceBatch batch, int firstInstanceLocation, BakeSlot? bake, IReadOnlyList<(int First, int Count, int Lod)> runs)
    {
        (_shape, _batch, _bake, _firstInstanceLocation) = (shape, batch, bake, firstInstanceLocation);
        _pendingAtlas = NoAtlas;
        _commands.Clear();

        int openFirst = 0, openCount = 0, openLod = -1;
        foreach (var (first, count, lod) in runs)
        {
            if (count <= 0)
                continue;
            // A run that begins where the open one ends, at the same level of detail, extends it.
            if (openCount > 0 && openFirst + openCount == first && openLod == lod)
            {
                openCount += count;
                continue;
            }
            if (openCount > 0)
                EmitRun(openFirst, openCount, openLod);
            (openFirst, openCount, openLod) = (first, count, lod);
        }
        if (openCount > 0)
            EmitRun(openFirst, openCount, openLod);
        stream.Draw(_commands);
    }

    void EmitRun(int first, int count, int lod)
    {
        var (firstIndex, indexCount) = _shape.Lod(lod);
        if (indexCount <= 0)
            return;
        if (_bake is not { } bake)
        {
            Emit(first, count, firstIndex, indexCount, NoAtlas);
            return;
        }

        int end = Math.Min(first + count, bake.AtlasOf.Length);
        for (int start = first; start < end;)
        {
            int atlas = bake.AtlasOf[start];
            int stop = start + 1;
            while (stop < end && bake.AtlasOf[stop] == atlas)
                stop++;
            Emit(start, stop - start, firstIndex, indexCount, atlas);
            start = stop;
        }
    }

    void Emit(int at, int instances, int firstIndex, int indexCount, int atlas)
    {
        _triangles += (long)indexCount / 3 * instances;
        _instances += instances;
        if (atlas != _pendingAtlas)
            SwitchAtlas(atlas);

        if (!InstancingContract.BaseInstance)
        {
            gl.Uniform1(_firstInstanceLocation, at);
            gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)indexCount, DrawElementsType.UnsignedInt,
                (void*)((nint)firstIndex * sizeof(uint)), (uint)instances);
            return;
        }
        _commands.Add(new DrawCommand((uint)indexCount, (uint)instances, (uint)firstIndex, 0, (uint)at));
    }

    // Commands queued for the last atlas are drawn before its texture is replaced; an unbaked instance takes the material's own back.
    void SwitchAtlas(int atlas)
    {
        stream.Draw(_commands);
        _pendingAtlas = atlas;
        if (atlas == NoAtlas)
            return;
        var bake = _bake!.Value;
        var texture = atlas >= 0 ? _batch.BakeAtlases[atlas] : bake.Own;
        bound.BindTexture(gl, bake.Unit, texture.Target, texture.Handle);
    }
}
