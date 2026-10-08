using System.Numerics;

namespace WildRenderingSharp.Graphics;

/// <summary>
/// A slot-indexed std140 scratch buffer, addressed the same way the decompiled shaders and the research docs address it:
/// <c>data[slot].component</c>.
/// </summary>
public sealed class Std140Block
{
    readonly Vector4[] _slots;

    public int SizeBytes { get; }

    public Std140Block(int sizeBytes)
    {
        if (sizeBytes <= 0 || sizeBytes % 16 != 0)
            throw new ArgumentException("std140 block size must be a positive multiple of 16 bytes", nameof(sizeBytes));
        SizeBytes = sizeBytes;
        _slots = new Vector4[sizeBytes / 16];
    }

    public Vector4 GetSlot(int slot) => _slots[slot];

    public void SetSlot(int slot, Vector4 value) => _slots[slot] = value;

    public void SetSlot(int slot, float x, float y, float z, float w) => _slots[slot] = new Vector4(x, y, z, w);

    public void SetVec3(int slot, Vector3 xyz, float w = 0f) => _slots[slot] = new Vector4(xyz, w);

    public float GetComponent(int slot, int component) => _slots[slot][component];

    public void SetComponent(int slot, int component, float value)
    {
        var v = _slots[slot];
        v[component] = value;
        _slots[slot] = v;
    }

    public float GetFloatAt(int byteOffset) => GetComponent(byteOffset / 16, (byteOffset % 16) / 4);

    public void SetFloatAt(int byteOffset, float value) => SetComponent(byteOffset / 16, (byteOffset % 16) / 4, value);

    public void SetVectorAt(int byteOffset, params ReadOnlySpan<float> components)
    {
        for (int i = 0; i < components.Length; i++)
            SetFloatAt(byteOffset + i * 4, components[i]);
    }

    public void WriteRows(int firstSlot, ReadOnlySpan<Vector4> rows)
    {
        for (int i = 0; i < rows.Length; i++)
            _slots[firstSlot + i] = rows[i];
    }

    public byte[] ToByteArray()
    {
        var bytes = new byte[SizeBytes];
        var span = bytes.AsSpan();
        for (int i = 0; i < _slots.Length; i++)
        {
            var s = _slots[i];
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(span[(i * 16)..], s.X);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(span[(i * 16 + 4)..], s.Y);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(span[(i * 16 + 8)..], s.Z);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(span[(i * 16 + 12)..], s.W);
        }
        return bytes;
    }
}
