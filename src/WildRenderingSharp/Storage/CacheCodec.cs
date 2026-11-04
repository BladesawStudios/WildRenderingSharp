using System.IO.Hashing;

namespace WildRenderingSharp.Storage;

/// <summary>Compresses, decompresses and names the bytes of the cache's files.</summary>
/// <remarks>
/// Files are compressed with zstd, textures at a lower effort than the rest since block-compressed data gains little from more. A texture shared between models is named by a
/// fast hash of its bytes and written once.
/// </remarks>
internal static class CacheCodec
{
    // Fast to write and faster to read; the cache is read far more often than it is made.
    const int Level = 3;

    [ThreadStatic] static ZstdSharp.Compressor? _compressor;
    [ThreadStatic] static ZstdSharp.Decompressor? _decompressor;

    // Compressed into a pooled buffer and copied out at its size, so a megabyte texture does not allocate the worst case on the large object heap.
    internal static byte[] Compress(ZstdSharp.Compressor compressor, byte[] bytes)
    {
        byte[] room = System.Buffers.ArrayPool<byte>.Shared.Rent(ZstdSharp.Compressor.GetCompressBound(bytes.Length));
        try
        {
            int written = compressor.Wrap(bytes, room);
            return room.AsSpan(0, written).ToArray();
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(room);
        }
    }

    internal static byte[] Compress(byte[] bytes) => Compress(_compressor ??= new ZstdSharp.Compressor(Level), bytes);

    // Texture data is block-compressed already and gains little from more effort.
    [ThreadStatic] static ZstdSharp.Compressor? _quickCompressor;
    internal static byte[] CompressTexture(byte[] bytes) => Compress(_quickCompressor ??= new ZstdSharp.Compressor(1), bytes);

    internal static byte[] Unpack(ReadOnlySpan<byte> bytes) => (_decompressor ??= new ZstdSharp.Decompressor()).Unwrap(bytes).ToArray();

    // What a texture's bytes are called in the shared folder: a fast non-cryptographic hash, since each model that uses a texture hashes its megabytes.
    internal static string ContentHash(byte[] bytes) => Convert.ToHexString(XxHash128.Hash(bytes));

    // Writes a shared texture, which is named by its content. Two models preparing the same texture at once both move their copy into place; the one refused finds the same file there, which is not a failure.
    internal static void WriteShared(string target, byte[] compressed)
    {
        try
        {
            AtomicFile.WriteAllBytes(target, compressed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && File.Exists(target))
        {
            // Another thread put the same file there first.
        }
    }
}
