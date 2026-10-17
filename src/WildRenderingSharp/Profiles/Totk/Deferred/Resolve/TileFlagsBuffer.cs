using Silk.NET.OpenGL;

namespace WildRenderingSharp.Profiles.Totk.Deferred.Resolve;

/// <summary>
/// The storage buffer (<c>vp_s0</c>) a field program's vertex stage reads to learn which screen tiles hold surfaces: instance i is kept only if a dword of
/// the tile's 16 bytes from 0x41C0 is 1. The game fills it with a compute pass; here the grid is one tile, so only the first tile's flag is set.
/// </summary>
sealed class TileFlagsBuffer(GL gl, int flagWord) : IDisposable
{
    // The dword of a tile's flags the resolve reads, and the one the pre-shading reads.
    public const int ResolveWord = 3, PreShadingWord = 0;

    readonly int _flagByte = 0x41C0 + flagWord * 4;

    uint _handle;

    public uint Handle => _handle != 0 ? _handle : _handle = Create();

    // Sets the flag again, for a program that also writes into the buffer it reads.
    public void Reset() => gl.NamedBufferSubData(Handle, _flagByte, BitConverter.GetBytes(1u));

    public void Dispose()
    {
        if (_handle != 0)
            gl.DeleteBuffer(_handle);
    }

    uint Create()
    {
        byte[] data = new byte[_flagByte + 16];
        BitConverter.TryWriteBytes(data.AsSpan(_flagByte), 1u);
        uint handle = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, handle);
        gl.BufferData<byte>(BufferTargetARB.ShaderStorageBuffer, data, BufferUsageARB.StaticDraw);
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
        return handle;
    }
}
