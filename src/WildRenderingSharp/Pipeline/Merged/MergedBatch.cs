using Silk.NET.OpenGL;
using System.Numerics;
using WildRenderingSharp.Pipeline.Drawing;

namespace WildRenderingSharp.Pipeline.Merged;

/// <summary>Where a batch's instances, and the table of atlas rectangles they look up, lie in the merged buffers.</summary>
internal sealed class MergedBatch(MergedInstances owner, int instanceBase, int stride)
{
    public int InstanceBase { get; } = instanceBase;

    // The vec4s in one instance as the merged draw reads it.
    public int Stride { get; } = stride;

    public int BakeBase { get; set; } = -1;

    public int BakeLength { get; set; }

    // Writes the batch's instances again, with the baked lighting it has now.
    public void Refresh(InstanceBatch batch) => owner.Refresh(batch, this);

    public void Release(InstanceBatch batch) => owner.Remove(batch);
}

/// <summary>Every batch's instances in one storage buffer, and every baked batch's table of atlas rectangles in another, so one multi-draw can draw batches of any model.</summary>
internal sealed class MergedInstances : IDisposable
{
    // The entries every table starts with: binding 0 is where a water program writes a per-pixel value, so no batch's own entries lie there.
    const int TableHead = 64;

    readonly RegionAllocator _instances = new(1 << 16);
    readonly RegionAllocator _table = new(1 << 12);
    readonly BindlessHandles _handles;
    readonly object _sync;

    public MergedInstances(GL gl, BindlessHandles handles, object sync)
    {
        (_handles, _sync) = (handles, sync);
        Instances = new GrowingBuffer(gl, 16, 1 << 16);
        BakeTable = new GrowingBuffer(gl, 16, 1 << 12);
        _table.Allocate(TableHead);
    }

    public GrowingBuffer Instances { get; }

    public GrowingBuffer BakeTable { get; }

    public MergedBatch Add(InstanceBatch batch)
    {
        lock (_sync)
        {
            int stride = batch.Stride + 1;
            int at = Allocate(_instances, Instances, Math.Max(1, batch.Count) * stride);
            var merged = new MergedBatch(this, at, stride);
            batch.Merged = merged;
            Refresh(batch, merged);
            return merged;
        }
    }

    public void Remove(InstanceBatch batch)
    {
        lock (_sync)
        {
            if (batch.Merged is not { } merged)
                return;
            _instances.Free(merged.InstanceBase, Math.Max(1, batch.Count) * merged.Stride);
            if (merged.BakeBase >= 0)
                _table.Free(merged.BakeBase, merged.BakeLength);
            batch.Merged = null;
        }
    }

    public void Refresh(InstanceBatch batch, MergedBatch merged)
    {
        lock (_sync)
        {
            if (merged.BakeBase >= 0)
                _table.Free(merged.BakeBase, merged.BakeLength);
            merged.BakeBase = -1;
            merged.BakeLength = 0;
            if (batch.BakeTableData is { } table && table.Length > TableHead)
            {
                merged.BakeLength = table.Length - TableHead;
                merged.BakeBase = Allocate(_table, BakeTable, merged.BakeLength);
                BakeTable.Write<Vector4>(merged.BakeBase, table.AsSpan(TableHead));
            }
            int shift = merged.BakeBase >= 0 ? merged.BakeBase - TableHead : 0;
            Instances.Write<Vector4>(merged.InstanceBase, batch.MergedRecords(_handles.Of, shift));
        }
    }

    public void Dispose()
    {
        Instances.Dispose();
        BakeTable.Dispose();
    }

    static int Allocate(RegionAllocator regions, GrowingBuffer buffer, int count)
    {
        int at = regions.Allocate(count);
        while (at < 0)
        {
            regions.Grow(Math.Max(regions.Capacity * 2, regions.Capacity + count));
            at = regions.Allocate(count);
        }
        buffer.EnsureCapacity(regions.Capacity);
        return at;
    }
}
