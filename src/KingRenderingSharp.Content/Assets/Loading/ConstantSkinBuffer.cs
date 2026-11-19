using System.Runtime.InteropServices;
using KingRenderingSharp.Gpu;

namespace KingRenderingSharp.Content.Assets.Loading;

/// <summary>The one vertex holding all skin weight on palette slot 0, shared by every shape in a GL context.</summary>
static class ConstantSkinBuffer
{
    sealed class Holder
    {
        public uint Buffer;
    }

    // The four blend attributes in order: weight 0, weight 1, index 0, index 1.
    static readonly float[] Vertex =
    [
        1, 0, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 0,
    ];

    public static uint For(GpuApi gl)
    {
        var holder = ContextState<Holder>.For(gl);
        lock (holder)
        {
            if (holder.Buffer == 0)
                holder.Buffer = GLBuffer.Create(gl, BufferTargetARB.ArrayBuffer, MemoryMarshal.AsBytes(Vertex.AsSpan()));
            return holder.Buffer;
        }
    }
}
