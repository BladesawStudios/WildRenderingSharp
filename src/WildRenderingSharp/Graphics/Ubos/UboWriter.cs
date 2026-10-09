using System.Buffers.Binary;
using System.Numerics;
using WildRenderingSharp.Graphics.Data;

namespace WildRenderingSharp.Graphics.Ubos;

/// <summary>
/// Builds one uniform block. Fields are addressed the way the decompiled shaders do, as <c>data[slot].component</c> with 16 bytes
/// to a slot, or by the byte offset the shader archive's reflection reports.
/// </summary>
public sealed class UboWriter
{
    readonly byte[] _bytes;

    public UboWriter(UboSpec spec)
    {
        if (spec.ByteSize <= 0 || spec.ByteSize % 16 != 0)
            throw new ArgumentException($"{spec.ShaderName}: a std140 block is a positive multiple of 16 bytes, not {spec.ByteSize}.", nameof(spec));
        Spec = spec;
        _bytes = new byte[spec.ByteSize];
    }

    public UboWriter(UboSpec spec, ReadOnlySpan<byte> start) : this(spec)
    {
        if (start.Length != spec.ByteSize)
            throw new ArgumentException($"{spec.ShaderName} is {spec.ByteSize} bytes, not {start.Length}.", nameof(start));
        start.CopyTo(_bytes);
    }

    // A writer that starts from a copy of an existing block, to change a few fields of it.
    public UboWriter(Ubo from) : this(from.Spec, from.Bytes.Span)
    {
    }

    public UboSpec Spec { get; }

    public void Set(int slot, int component, float value) => SetAt(slot * 16 + component * 4, value);

    public void Set(int slot, float x, float y, float z, float w) => SetAt(slot * 16, x, y, z, w);

    public void Set(int slot, Vector4 value) => Set(slot, value.X, value.Y, value.Z, value.W);

    public void Set(int slot, Vector3 xyz, float w) => Set(slot, xyz.X, xyz.Y, xyz.Z, w);

    // Writes three components and leaves the fourth as it is.
    public void SetXyz(int slot, Vector3 xyz) => SetAt(slot * 16, xyz.X, xyz.Y, xyz.Z);

    // Writes an integer into a float component, the way the shaders reinterpret it.
    public void SetInt(int slot, int component, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(_bytes.AsSpan(slot * 16 + component * 4), value);

    public void SetAt(int byteOffset, float value) => BinaryPrimitives.WriteSingleLittleEndian(_bytes.AsSpan(byteOffset), value);

    public void SetAt(int byteOffset, params ReadOnlySpan<float> values)
    {
        for (int i = 0; i < values.Length; i++)
            SetAt(byteOffset + i * 4, values[i]);
    }

    public void Set(UboMatrix field, Matrix4x4 matrix) => Set(field, GpuMatrix.Rows(matrix, field.Rows));

    // Writes rows that are already in the shader's order, such as a placement's three rows.
    public void Set(UboMatrix field, ReadOnlySpan<Vector4> rows)
    {
        if (rows.Length != field.Rows)
            throw new ArgumentException($"{Spec.ShaderName}: a field of {field.Rows} rows was given {rows.Length}.", nameof(rows));
        for (int i = 0; i < rows.Length; i++)
            Set(field.Slot + i, rows[i]);
    }

    public float Get(int slot, int component) => BinaryPrimitives.ReadSingleLittleEndian(_bytes.AsSpan(slot * 16 + component * 4));

    public Vector4 Get(int slot) => new(Get(slot, 0), Get(slot, 1), Get(slot, 2), Get(slot, 3));

    // The finished block. The writer hands over its bytes, so it is not written to afterwards.
    public Ubo ToUbo(string key) => new(key, Spec, _bytes);
}
