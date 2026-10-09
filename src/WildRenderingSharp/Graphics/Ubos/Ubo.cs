namespace WildRenderingSharp.Graphics.Ubos;

/// <summary>The bytes of one uniform block, and the key of the GL buffer they are kept in between frames.</summary>
public sealed class Ubo
{
    public Ubo(string key, UboSpec spec, ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length != spec.ByteSize)
            throw new ArgumentException($"{spec.ShaderName} is {spec.ByteSize} bytes, not {bytes.Length}.", nameof(bytes));
        Key = key;
        Spec = spec;
        Bytes = bytes;
    }

    public string Key { get; }

    public UboSpec Spec { get; }

    public ReadOnlyMemory<byte> Bytes { get; }
}
