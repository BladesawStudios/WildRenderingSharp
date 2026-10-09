using Silk.NET.OpenGL;

namespace WildRenderingSharp.Profiles.Totk.Deferred.Resolve;

/// <summary>
/// The storage buffer (<c>vp_s0</c>) <c>field_hybrid</c>'s vertex stage reads to learn which screen tiles hold surfaces: instance i is kept only if the
/// dword at byte 0x41C0 + 16 i + 12 is 1. The game fills it with a compute pass; here the grid is one tile, so only the first flag is set.
/// </summary>
sealed class TileFlagsBuffer(GL gl) : IDisposable
{
    const int FlagByte = 0x41C0 + 12;

    uint _handle;

    public uint Handle => _handle != 0 ? _handle : _handle = Create();

    public void Dispose()
    {
        if (_handle != 0)
            gl.DeleteBuffer(_handle);
    }

    uint Create()
    {
        byte[] data = new byte[FlagByte + 16];
        BitConverter.TryWriteBytes(data.AsSpan(FlagByte), 1u);
        uint handle = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, handle);
        gl.BufferData<byte>(BufferTargetARB.ShaderStorageBuffer, data, BufferUsageARB.StaticDraw);
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
        return handle;
    }
}
