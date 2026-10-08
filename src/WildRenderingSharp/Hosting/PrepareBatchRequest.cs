namespace WildRenderingSharp.Hosting;

/// <summary>A request to prepare many actors or models at once - a map section's worth.</summary>
public sealed record PrepareBatchRequest(
    string RomfsRoot,
    IReadOnlyList<string> ActorOrModelNames,
    CacheLayout Cache,
    IReadOnlyList<string>? ModRomfsLayers = null,
    bool ImportAnimations = true,
    bool Force = false,
    int Parallelism = 0)
{
    public int EffectiveParallelism => Parallelism > 0 ? Parallelism : DefaultParallelism;

    public static int DefaultParallelism => Math.Clamp(Environment.ProcessorCount - 1, 1, 12);
}
