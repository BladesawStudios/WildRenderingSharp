using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline.Merged;

/// <summary>A storage buffer of fixed-size elements that is made larger, its contents carried over, when more room is needed.</summary>
internal sealed unsafe class GrowingBuffer : IDisposable
{
    readonly GL _gl;
    readonly int _elementBytes;

    public GrowingBuffer(GL gl, int elementBytes, int initialElements)
    {
        _gl = gl;
        _elementBytes = elementBytes;
        Capacity = initialElements;
        Handle = Create(initialElements);
    }

    public uint Handle { get; private set; }

    public int Capacity { get; private set; }

    // Makes the buffer at least this long, by doubling; returns whether it changed, which is when anything bound to it must be bound again.
    public bool EnsureCapacity(int elements)
    {
        if (elements <= Capacity)
            return false;
        int grown = Math.Max(elements, Capacity * 2);
        uint bigger = Create(grown);
        _gl.BindBuffer(BufferTargetARB.CopyReadBuffer, Handle);
        _gl.BindBuffer(BufferTargetARB.CopyWriteBuffer, bigger);
        _gl.CopyBufferSubData(CopyBufferSubDataTarget.CopyReadBuffer, CopyBufferSubDataTarget.CopyWriteBuffer, 0, 0, (nuint)((long)Capacity * _elementBytes));
        _gl.DeleteBuffer(Handle);
        (Handle, Capacity) = (bigger, grown);
        return true;
    }

    public void Write<T>(int element, ReadOnlySpan<T> data, int extraBytes = 0) where T : unmanaged
    {
        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, Handle);
        fixed (T* p = data)
            _gl.BufferSubData(BufferTargetARB.ShaderStorageBuffer, (nint)((long)element * _elementBytes + extraBytes), (nuint)(data.Length * sizeof(T)), p);
    }

    // Writes bytes at a byte offset, which is not always a whole element's.
    public void WriteBytes(long byteOffset, ReadOnlySpan<byte> data) => Write((int)(byteOffset / _elementBytes), data, (int)(byteOffset % _elementBytes));

    public void Dispose() => _gl.DeleteBuffer(Handle);

    uint Create(int elements)
    {
        uint buffer = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, buffer);
        _gl.BufferData(BufferTargetARB.ShaderStorageBuffer, (nuint)((long)elements * _elementBytes), null, BufferUsageARB.DynamicDraw);
        return buffer;
    }
}
