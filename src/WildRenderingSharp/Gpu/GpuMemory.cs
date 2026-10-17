namespace WildRenderingSharp.Gpu;

/// <summary>What the renderer has put on the card, by kind, counted as it is made and let go of, so a log line can say where the memory went.</summary>
public static class GpuMemory
{
    static long _textures, _shapeBuffers, _arena, _arenaUsed, _materialBlocks, _textureCount, _shapeCount;

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

    internal static void MergedUsed(long bytes) => Interlocked.Add(ref _arenaUsed, bytes);

    internal static void Material(long bytes) => Interlocked.Add(ref _materialBlocks, bytes);

    /// <summary>What the card says about its own memory (NVIDIA only): free now, and how much it has had to move out of video memory to make room, which is the sign of a scene that does not fit.</summary>
    public static string Card(Silk.NET.OpenGL.GL gl)
    {
        if (!gl.IsExtensionPresent("GL_NVX_gpu_memory_info"))
            return "card memory unknown";
        gl.GetInteger((Silk.NET.OpenGL.GLEnum)0x9047, out int dedicated);
        gl.GetInteger((Silk.NET.OpenGL.GLEnum)0x9049, out int free);
        gl.GetInteger((Silk.NET.OpenGL.GLEnum)0x904A, out int evictions);
        gl.GetInteger((Silk.NET.OpenGL.GLEnum)0x904B, out int evicted);
        return $"card {free / 1024:N0} of {dedicated / 1024:N0} MB free, {evictions:N0} evictions moving {evicted / 1024:N0} MB out";
    }

    /// <summary>The card's own video memory in megabytes, or 0 when it does not say (NVIDIA only).</summary>
    public static int DedicatedMegabytes(Silk.NET.OpenGL.GL gl)
    {
        if (!gl.IsExtensionPresent("GL_NVX_gpu_memory_info"))
            return 0;
        gl.GetInteger((Silk.NET.OpenGL.GLEnum)0x9047, out int dedicated);
        return dedicated / 1024;
    }

    public static string Summary()
    {
        static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):N0} MB";
        return $"textures {Mb(Textures)} ({Interlocked.Read(ref _textureCount):N0}), shapes' own buffers {Mb(ShapeBuffers)} ({Interlocked.Read(ref _shapeCount):N0}), merged geometry {Mb(Interlocked.Read(ref _arenaUsed))} used of {Mb(Arena)}, material blocks {Mb(MaterialBlocks)}";
    }
}
