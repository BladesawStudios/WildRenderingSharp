namespace WildRenderingSharp.Hosting;

/// <summary>A request to prepare many actors or models at once - a map section's worth.</summary>
/// <remarks>
/// The other parameters mean what they do on <see cref="PrepareRequest"/>, and apply to every name.
/// </remarks>
public sealed record PrepareBatchRequest(
    string RomfsRoot,
    IReadOnlyList<string> ActorOrModelNames,
    CacheLayout Cache,
    IReadOnlyList<string>? ModRomfsLayers = null,
    bool ImportAnimations = true,
    bool Force = false,
    int Parallelism = 0)
{
    /// <summary><see cref="Parallelism"/>, or the default for this machine when it is 0.</summary>
    public int EffectiveParallelism => Parallelism > 0 ? Parallelism : DefaultParallelism;

    /// <summary>
    /// One model per core, leaving one for the host, and no more than twelve: each model in flight holds its decompressed BFRES and
    /// textures, so memory, not cores, is what runs out first.
    /// </summary>
    public static int DefaultParallelism => Math.Clamp(Environment.ProcessorCount - 1, 1, 12);
}
