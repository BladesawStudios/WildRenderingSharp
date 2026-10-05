using System.Numerics;

namespace WildRenderingSharp.Shaders.Common;

/// <summary>
/// A slot-indexed std140 scratch buffer, addressed the same way the decompiled shaders and the
/// research docs address it: <c>data[slot].component</c>. Every std140 field this codebase deals
/// with (Context/Env/SceneMat, all recovered from the game's own compiled shaders) is either a
/// float, vec3 or vec4 packed into one 16-byte slot, or a matrix laid out as consecutive
/// full-slot rows - so a flat <see cref="Vector4"/> array indexed by slot is the whole
/// representation std140 needs here, with no per-field alignment logic to get wrong.
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

    /// <summary>
    /// Reads/writes a single float at an arbitrary byte offset (4-byte aligned - every field this
    /// codebase deals with is), for the fields whose reflection-recovered offset isn't itself
    /// 16-byte slot-aligned (e.g. several <c>gsys_scene_material</c> fields sit mid-slot). Prefer
    /// <see cref="SetSlot(int,Vector4)"/>/<see cref="SetVec3"/> when a field IS slot-aligned; this
    /// is the general fallback that works everywhere, aligned or not.
    /// </summary>
    public float GetFloatAt(int byteOffset) => GetComponent(byteOffset / 16, (byteOffset % 16) / 4);

    public void SetFloatAt(int byteOffset, float value) => SetComponent(byteOffset / 16, (byteOffset % 16) / 4, value);

    /// <summary>Writes consecutive floats starting at an arbitrary byte offset - a vec2/vec3/vec4 field that isn't slot-aligned.</summary>
    public void SetVectorAt(int byteOffset, params ReadOnlySpan<float> components)
    {
        for (int i = 0; i < components.Length; i++)
            SetFloatAt(byteOffset + i * 4, components[i]);
    }

    /// <summary>
    /// Writes consecutive full-slot rows starting at <paramref name="firstSlot"/> - how a mat3x4
    /// (3 rows) or mat4 (4 rows) is laid out in std140: each row occupies one 16-byte slot
    /// regardless of the matrix's real column count, because std140 always rounds a matrix
    /// column/row up to vec4 size.
    /// </summary>
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

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < SizeBytes)
            throw new ArgumentException($"destination must be at least {SizeBytes} bytes", nameof(destination));
        ToByteArray().CopyTo(destination);
    }
}
