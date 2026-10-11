using Silk.NET.OpenGL;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Pipeline.Resources;

/// <summary>
/// What every decompiled shader expects bound whatever the scene: the support buffer, and an empty storage buffer at binding 0 for the
/// shaders that declare one and never use it.
/// </summary>
internal sealed class DecompilerBindings : IDisposable
{
    const int StorageBytes = 65536;

    readonly GL _gl;
    readonly GLResourceCache _resources;
    readonly uint _zeroStorage;

    public DecompilerBindings(GL gl, GLResourceCache resources)
    {
        _gl = gl;
        _resources = resources;
        _zeroStorage = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _zeroStorage);
        gl.BufferData(BufferTargetARB.ShaderStorageBuffer, new byte[StorageBytes], BufferUsageARB.StaticDraw);
        BindStorage();
    }

    // Called every frame, since a host drawing in between may use storage binding 0 itself.
    public void Bind()
    {
        BindStorage();
        _resources.Bind(SupportBuffer.Block);
    }

    void BindStorage() => _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, _zeroStorage);

    public void Dispose() => _gl.DeleteBuffer(_zeroStorage);
}
