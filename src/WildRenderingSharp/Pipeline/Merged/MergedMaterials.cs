using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Materials;

namespace WildRenderingSharp.Pipeline.Merged;

/// <summary>Every shape's material block, one after another in a storage buffer, each in a slot of the same size, and kept the same as the block it copies.</summary>
internal sealed class MergedMaterials : IDisposable
{
    readonly RegionAllocator _slots = new(MergedLayout.MaterialVec4s * 256);
    readonly object _sync;

    public MergedMaterials(GL gl, object sync)
    {
        _sync = sync;
        Buffer = new GrowingBuffer(gl, 16, MergedLayout.MaterialVec4s * 256);
    }

    public GrowingBuffer Buffer { get; }

    // The first vec4 of a new slot holding the block, which from now on follows every change to it.
    public int Add(MaterialBlock block)
    {
        int at = _slots.Allocate(MergedLayout.MaterialVec4s);
        if (at < 0)
        {
            _slots.Grow(_slots.Capacity * 2);
            at = _slots.Allocate(MergedLayout.MaterialVec4s);
        }
        Buffer.EnsureCapacity(_slots.Capacity);

        int bytes = MergedLayout.MaterialVec4s * 16;
        var slot = new byte[bytes];
        block.Authored[..Math.Min(block.Authored.Length, bytes)].CopyTo(slot);
        Buffer.WriteBytes((long)at * 16, slot);
        block.Copy = (offset, data) =>
        {
            lock (_sync)
            {
                if (offset < bytes)
                    Buffer.WriteBytes((long)at * 16 + offset, data[..Math.Min(data.Length, bytes - offset)]);
            }
        };
        return at;
    }

    public void Remove(MaterialBlock block, int at)
    {
        block.Copy = null;
        _slots.Free(at, MergedLayout.MaterialVec4s);
    }

    public void Dispose() => Buffer.Dispose();
}
