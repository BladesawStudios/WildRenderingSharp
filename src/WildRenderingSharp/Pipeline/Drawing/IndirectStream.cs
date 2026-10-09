using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline.Drawing;

// One command of a multi-draw: GL's DrawElementsIndirectCommand.
/// <summary>One command of a multi-draw: GL's DrawElementsIndirectCommand.</summary>
[StructLayout(LayoutKind.Sequential)]
readonly record struct DrawCommand(uint Count, uint InstanceCount, uint FirstIndex, int BaseVertex, uint BaseInstance);

/// <summary>A ring of draw-indirect memory that multi-draw commands are written into and drawn from, fenced chunk by chunk when it is persistently mapped.</summary>
sealed unsafe class IndirectStream : IDisposable
{
    const int Capacity = 8 << 20, Chunks = 8, ChunkSize = Capacity / Chunks;

    readonly GL _gl;
    readonly uint _buffer;
    readonly byte* _mapped;
    readonly nint[] _fences = new nint[Chunks];
    int _offset, _chunk;

    public IndirectStream(GL gl)
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

    // Draws the commands as one multi-draw and empties the list.
    public void Draw(List<DrawCommand> commands)
    {
        if (commands.Count == 0)
            return;
        nint offset = Upload(CollectionsMarshal.AsSpan(commands));
        _gl.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, (void*)offset, (uint)commands.Count, (uint)sizeof(DrawCommand));
        commands.Clear();
    }

    public void Dispose()
    {
        if (_mapped is not null)
        {
            _gl.BindBuffer(BufferTargetARB.DrawIndirectBuffer, _buffer);
            _gl.UnmapBuffer(BufferTargetARB.DrawIndirectBuffer);
        }
        foreach (nint fence in _fences)
            if (fence != 0)
                _gl.DeleteSync(fence);
        _gl.DeleteBuffer(_buffer);
    }

    nint Upload(ReadOnlySpan<DrawCommand> commands)
    {
        int bytes = commands.Length * sizeof(DrawCommand);
        _gl.BindBuffer(BufferTargetARB.DrawIndirectBuffer, _buffer);
        if (_mapped is null)
            UploadCopy(commands, bytes);
        else
            UploadMapped(commands, bytes);
        nint at = _offset;
        _offset += (bytes + 15) & ~15;
        return at;
    }

    void UploadCopy(ReadOnlySpan<DrawCommand> commands, int bytes)
    {
        if (_offset + bytes > Capacity)
        {
            _gl.BufferData(BufferTargetARB.DrawIndirectBuffer, (nuint)Capacity, null, BufferUsageARB.StreamDraw);
            _offset = 0;
        }
        fixed (DrawCommand* p = commands)
            _gl.BufferSubData(BufferTargetARB.DrawIndirectBuffer, _offset, (nuint)bytes, p);
    }

    void UploadMapped(ReadOnlySpan<DrawCommand> commands, int bytes)
    {
        if (bytes > ChunkSize)
            throw new InvalidOperationException($"{commands.Length} indirect commands in one call");
        if (_offset + bytes > (_chunk + 1) * ChunkSize)
            AdvanceChunk();
        commands.CopyTo(new Span<DrawCommand>(_mapped + _offset, commands.Length));
    }

    // Fences the chunk just filled and moves to the next, waiting until the card is past that chunk's last use.
    void AdvanceChunk()
    {
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
}
