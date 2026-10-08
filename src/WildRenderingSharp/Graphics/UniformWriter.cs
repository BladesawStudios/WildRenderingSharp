using System.Buffers.Binary;
using System.Numerics;

namespace WildRenderingSharp.Graphics;

/// <summary>Writes floats into a std140 byte block by (16-byte slot, component), the way the decompiled shaders address it.</summary>
public readonly struct UniformWriter(byte[] buffer)
{
    public void Set(int slot, int component, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(slot * 16 + component * 4), value);

    public void Set(int slot, Vector3 xyz)
    {
        Set(slot, 0, xyz.X);
        Set(slot, 1, xyz.Y);
        Set(slot, 2, xyz.Z);
    }

    public void SetRows(int firstSlot, ReadOnlySpan<Vector4> rows)
    {
        for (int i = 0; i < rows.Length; i++)
        {
            Set(firstSlot + i, 0, rows[i].X);
            Set(firstSlot + i, 1, rows[i].Y);
            Set(firstSlot + i, 2, rows[i].Z);
            Set(firstSlot + i, 3, rows[i].W);
        }
    }
}
