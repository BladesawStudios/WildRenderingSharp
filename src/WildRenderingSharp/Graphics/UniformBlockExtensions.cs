
namespace WildRenderingSharp.Graphics;

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
