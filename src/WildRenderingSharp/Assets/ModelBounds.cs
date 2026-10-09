using System.Numerics;
using System.Runtime.InteropServices;

namespace WildRenderingSharp.Assets;

/// <summary>The axis-aligned bounds and the positions of every vertex of a model's shapes.</summary>
sealed class ModelBounds
{
    public Vector3 Min { get; private set; } = new(float.MaxValue);

    public Vector3 Max { get; private set; } = new(float.MinValue);

    public List<Vector3> Positions { get; } = [];

    // Reads the position from the first twelve bytes of each vertex.
    public void Add(ReadOnlySpan<byte> vertexBytes, int stride)
    {
        for (int i = 0; i < vertexBytes.Length / stride; i++)
        {
            var position = MemoryMarshal.Cast<byte, Vector3>(vertexBytes.Slice(i * stride, 12))[0];
            Min = Vector3.Min(Min, position);
            Max = Vector3.Max(Max, position);
            Positions.Add(position);
        }
    }
}
