using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline.Gpu;

namespace WildRenderingSharp.Assets.Materials;

/// <summary>A material's <c>gsys_material</c> uniform block: its authored bytes and the padded GL buffer they are shown through.</summary>
public sealed class MaterialBlock : IDisposable
{
    // Large enough for any shading model's declared block size.
    public const int BufferSize = 65536;

    readonly GL _gl;
    byte[] _authored;

    public MaterialBlock(GL gl, ReadOnlySpan<byte> authored)
    {
        _gl = gl;
        _authored = authored.ToArray();
        Handle = GLBuffer.Create(gl, BufferTargetARB.UniformBuffer, Padded(authored), BufferUsageARB.DynamicDraw);
    }

    public uint Handle { get; }

    public ReadOnlySpan<byte> Authored => _authored;

    // Whether the buffer currently shows something other than the authored bytes.
    public bool IsPatched { get; private set; }

    // A block read from an exported file; a missing file is an all-zero block.
    public static MaterialBlock FromFile(GL gl, string path) => new(gl, File.Exists(path) ? File.ReadAllBytes(path) : []);

    internal static byte[] Padded(ReadOnlySpan<byte> authored)
    {
        var padded = new byte[BufferSize];
        authored[..Math.Min(authored.Length, BufferSize)].CopyTo(padded);
        return padded;
    }

    // Shows bytes in place of the authored ones, leaving the rest of the padded buffer alone.
    public void Show(ReadOnlySpan<byte> bytes)
    {
        Upload(0, bytes);
        IsPatched = true;
    }

    public void Restore()
    {
        if (!IsPatched)
            return;
        Upload(0, _authored);
        IsPatched = false;
    }

    // Changes one authored word, which is what the block shows from then on.
    public void SetAuthored(int offset, int value)
    {
        BitConverter.TryWriteBytes(_authored.AsSpan(offset, 4), value);
        Upload(offset, _authored.AsSpan(offset, 4));
    }

    public void Dispose() => _gl.DeleteBuffer(Handle);

    unsafe void Upload(int offset, ReadOnlySpan<byte> bytes)
    {
        _gl.BindBuffer(BufferTargetARB.UniformBuffer, Handle);
        fixed (byte* ptr = bytes)
            _gl.BufferSubData(BufferTargetARB.UniformBuffer, offset, (nuint)bytes.Length, ptr);
        _gl.BindBuffer(BufferTargetARB.UniformBuffer, 0);
    }
}
