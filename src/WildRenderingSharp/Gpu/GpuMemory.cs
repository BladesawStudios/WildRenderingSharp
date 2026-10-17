namespace WildRenderingSharp.Gpu;

/// <summary>What the renderer has put on the card, by kind, counted as it is made and let go of, so a log line can say where the memory went.</summary>
public static class GpuMemory
{
    static long _textures, _shapeBuffers, _arena, _materialBlocks, _textureCount, _shapeCount;

    public static long Textures => Interlocked.Read(ref _textures);

    public static long ShapeBuffers => Interlocked.Read(ref _shapeBuffers);

    public static long Arena => Interlocked.Read(ref _arena);

    public static long MaterialBlocks => Interlocked.Read(ref _materialBlocks);

    internal static void Texture(long bytes)
    {
        Interlocked.Add(ref _textures, bytes);
        Interlocked.Add(ref _textureCount, Math.Sign(bytes));
    }

    internal static void Shape(long bytes)
    {
        Interlocked.Add(ref _shapeBuffers, bytes);
        Interlocked.Add(ref _shapeCount, Math.Sign(bytes));
    }

    internal static void Merged(long bytes) => Interlocked.Add(ref _arena, bytes);

    internal static void Material(long bytes) => Interlocked.Add(ref _materialBlocks, bytes);

    public static string Summary()
    {
        static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):N0} MB";
        return $"textures {Mb(Textures)} ({Interlocked.Read(ref _textureCount):N0}), shapes' own buffers {Mb(ShapeBuffers)} ({Interlocked.Read(ref _shapeCount):N0}), merged geometry {Mb(Arena)}, material blocks {Mb(MaterialBlocks)}";
    }
}
