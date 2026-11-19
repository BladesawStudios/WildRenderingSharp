
namespace KingRenderingSharp.Gpu;

/// <summary>Small GL-buffer helpers shared by the model loader and the deferred pipeline.</summary>
internal static class GLBuffer
{
    public static uint Create(GpuApi gl, BufferTargetARB target, ReadOnlySpan<byte> data, BufferUsageARB usage = BufferUsageARB.StaticDraw)
    {
        uint handle = gl.GenBuffer();
        gl.BindBuffer(target, handle);
        gl.BufferData(target, data, usage);
        return handle;
    }
}
