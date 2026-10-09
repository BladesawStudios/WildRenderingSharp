using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline.Gpu;

namespace WildRenderingSharp.Assets.Loading;

/// <summary>The one vertex holding all skin weight on palette slot 0, shared by every shape in a GL context.</summary>
static class ConstantSkinBuffer
{
    static readonly ConditionalWeakTable<GL, StrongBox<uint>> Buffers = new();

    // The four blend attributes in order: weight 0, weight 1, index 0, index 1.
    static readonly float[] Vertex =
    [
        1, 0, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 0,
    ];

    public static uint For(GL gl)
    {
        var box = Buffers.GetValue(gl, _ => new StrongBox<uint>());
        lock (box)
        {
            if (box.Value == 0)
                box.Value = GLBuffer.Create(gl, BufferTargetARB.ArrayBuffer, MemoryMarshal.AsBytes(Vertex.AsSpan()));
            return box.Value;
        }
    }
}
