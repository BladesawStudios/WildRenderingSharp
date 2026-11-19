using KingRenderingSharp.Core.Storage;
using KingRenderingSharp.Gpu;

namespace KingRenderingSharp.Content.Assets.Materials;

/// <summary>A material's <c>gsys_material</c> uniform block: its authored bytes and the padded GL buffer they are shown through.</summary>
internal sealed class MaterialBlock : IDisposable
{
    // Told of every change to the buffer, by the offset it starts at, so a copy of the block kept elsewhere stays the same.
    public delegate void Mirror(int offset, ReadOnlySpan<byte> bytes);

    public Mirror? Copy { get; set; }

    // Larger than any game program reads: they index the block with constants, none past the 42nd vec4, and what they read past the authored bytes is zero. A buffer the size
    // a block may be declared at took 64 KiB for each shape of a whole map for a few hundred bytes of block.
    public const int BufferSize = 4096;

    readonly GpuApi _gl;
    byte[] _authored;

    // The buffer is made when something first binds it. The merged draws read a shape's block from their own arena, so a scene drawn that way never makes
    // one, which would otherwise be 64 KiB of buffer for each of thousands of shapes whose authored block is a few hundred bytes.
    uint _handle;

    // What the block shows in place of the authored bytes, kept until the buffer exists to show it in.
    byte[]? _shown;

    public MaterialBlock(GpuApi gl, ReadOnlySpan<byte> authored)
    {
        _gl = gl;
        _authored = authored.ToArray();
        Id = gl.NextId();
    }

    /// <summary>Tells blocks apart without making a buffer, for ordering draws.</summary>
    public int Id { get; }

    /// <summary>The GL buffer, made on the first use.</summary>
    public uint Handle
    {
        get
        {
            if (_handle == 0)
            {
                _handle = GLBuffer.Create(_gl, BufferTargetARB.UniformBuffer, Padded(_shown ?? _authored), BufferUsageARB.DynamicDraw);
                _gl.Memory.Material(BufferSize);
            }
            return _handle;
        }
    }

    public ReadOnlySpan<byte> Authored => _authored;

    // What the block shows now: the authored bytes, or what was shown in their place.
    public ReadOnlySpan<byte> Shown => _shown ?? _authored;

    // Whether the buffer currently shows something other than the authored bytes.
    public bool IsPatched { get; private set; }

    // A block read from an exported file; a missing file is an all-zero block.
    public static MaterialBlock FromFile(GpuApi gl, string path) => new(gl, CacheFiles.Exists(path) ? CacheFiles.ReadAllBytes(path) : []);

    // A block read from a store; a file the store lacks is an all-zero block.
    public static MaterialBlock From(GpuApi gl, IContentFiles files, string name) => new(gl, files.Exists(name) ? files.ReadBytes(name) : []);

    internal static byte[] Padded(ReadOnlySpan<byte> authored)
    {
        var padded = new byte[BufferSize];
        authored[..Math.Min(authored.Length, BufferSize)].CopyTo(padded);
        return padded;
    }

    // Shows bytes in place of the authored ones, leaving the rest of the padded buffer alone.
    public void Show(ReadOnlySpan<byte> bytes)
    {
        _shown = bytes.ToArray();
        Upload(0, bytes);
        IsPatched = true;
    }

    public void Restore()
    {
        if (!IsPatched)
            return;
        _shown = null;
        Upload(0, _authored);
        IsPatched = false;
    }

    // Changes one authored word, which is what the block shows from then on.
    public void SetAuthored(int offset, int value)
    {
        BitConverter.TryWriteBytes(_authored.AsSpan(offset, 4), value);
        if (_shown is not null && offset + 4 <= _shown.Length)
            BitConverter.TryWriteBytes(_shown.AsSpan(offset, 4), value);
        Upload(offset, _authored.AsSpan(offset, 4));
    }

    public void Dispose()
    {
        if (_handle != 0)
        {
            _gl.DeleteBuffer(_handle);
            _gl.Memory.Material(-BufferSize);
        }
        _handle = 0;
    }

    unsafe void Upload(int offset, ReadOnlySpan<byte> bytes)
    {
        if (_handle != 0)
        {
            _gl.BindBuffer(BufferTargetARB.UniformBuffer, _handle);
            int length = Math.Min(bytes.Length, BufferSize - offset);
            fixed (byte* ptr = bytes)
                if (length > 0)
                    _gl.BufferSubData(BufferTargetARB.UniformBuffer, offset, (nuint)length, ptr);
            _gl.BindBuffer(BufferTargetARB.UniformBuffer, 0);
        }
        Copy?.Invoke(offset, bytes);
    }
}
