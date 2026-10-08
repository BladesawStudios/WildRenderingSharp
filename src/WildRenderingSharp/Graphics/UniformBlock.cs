using Silk.NET.OpenGL;

namespace WildRenderingSharp.Graphics;

/// <summary>
/// One uniform buffer a profile wants bound: its bytes, the binding point the game's shaders read
/// it at, and the key its GL buffer is kept under between frames. A block with no data stands for
/// the shared zeroed buffer, for shaders that declare a block nothing fills.
/// </summary>
public readonly record struct UniformBlock(string Key, uint Binding, byte[]? Data)
{
    public static UniformBlock Zeroed(uint binding) => new("", binding, null);
}

public static class UniformBlockExtensions
{
    public static void Bind(this UniformBlock block, Pipeline.GLResourceCache resources)
    {
        if (block.Data is null)
            resources.BindZeroUbo(block.Binding);
        else
            resources.Ubo(block.Key, block.Data, block.Binding);
    }

    public static void Bind(this IEnumerable<UniformBlock> blocks, Pipeline.GLResourceCache resources)
    {
        foreach (var block in blocks)
            block.Bind(resources);
    }
}
