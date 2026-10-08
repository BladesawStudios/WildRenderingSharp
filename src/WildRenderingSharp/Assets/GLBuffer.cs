using Silk.NET.OpenGL;

namespace WildRenderingSharp.Assets;

/// <summary>Small GL-buffer helpers shared by the model loader and the deferred pipeline.</summary>
public static class GLBuffer
{
    public static uint Create(GL gl, BufferTargetARB target, ReadOnlySpan<byte> data, BufferUsageARB usage = BufferUsageARB.StaticDraw)
    {
        uint handle = gl.GenBuffer();
        gl.BindBuffer(target, handle);
        gl.BufferData(target, data, usage);
        return handle;
    }

    public static uint CreatePaddedUniformBuffer(GL gl, ReadOnlySpan<byte> data, int totalSize = 65536)
    {
        var padded = new byte[totalSize];
        data[..Math.Min(data.Length, totalSize)].CopyTo(padded);
        return Create(gl, BufferTargetARB.UniformBuffer, padded, BufferUsageARB.DynamicDraw);
    }

    public static void UpdatePaddedUniformBuffer(GL gl, uint handle, ReadOnlySpan<byte> data, int totalSize = 65536)
    {
        var padded = new byte[totalSize];
        data[..Math.Min(data.Length, totalSize)].CopyTo(padded);
        gl.BindBuffer(BufferTargetARB.UniformBuffer, handle);
        gl.BufferData(BufferTargetARB.UniformBuffer, new ReadOnlySpan<byte>(padded), BufferUsageARB.DynamicDraw);
    }
}
