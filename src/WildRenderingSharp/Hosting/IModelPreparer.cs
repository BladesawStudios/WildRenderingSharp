namespace WildRenderingSharp.Hosting;

/// <summary>
/// One request to prepare an actor or model into a <see cref="CacheLayout"/>.
/// </summary>
/// <param name="RomfsRoot">The base game's romfs.</param>
/// <param name="ActorOrModelName">
/// An actor name (preferred - its pack names the real model and its animation archives exactly)
/// or a bare model name for something with no actor pack.
/// </param>
/// <param name="Cache">Where to write.</param>
/// <param name="ModRomfsLayers">
/// Mod romfs folders layered over <paramref name="RomfsRoot"/>, highest priority first. Replacement
/// is per file, like an emulator's LayeredFS, so a texture-only mod applies to an unmodded model.
/// </param>
/// <param name="ImportAnimations">False skips the actor's animation archives - much faster, for a static look.</param>
/// <param name="Force">Prepare even if the cache says the model is already up to date.</param>
public sealed record PrepareRequest(
    string RomfsRoot,
    string ActorOrModelName,
    CacheLayout Cache,
    IReadOnlyList<string>? ModRomfsLayers = null,
    bool ImportAnimations = true,
    bool Force = false);

/// <summary>
/// A request to prepare many actors or models at once - a map section's worth.
/// </summary>
/// <param name="ActorOrModelNames">What to prepare. Names that resolve to the same model are prepared once.</param>
/// <param name="Parallelism">How many models to prepare at the same time; 0 picks from the processor count.</param>
/// <remarks>The other parameters mean what they do on <see cref="PrepareRequest"/>, and apply to every name.</remarks>
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
    /// One model per core, leaving one for the host, and no more than twelve: each model in flight
    /// holds its decompressed BFRES and textures, so memory, not cores, is what runs out first.
    /// </summary>
    public static int DefaultParallelism => Math.Clamp(Environment.ProcessorCount - 1, 1, 12);
}

/// <summary>How one name of a <see cref="PrepareBatchRequest"/> went.</summary>
/// <param name="ModelName">The resolved model name on success, as <see cref="IModelPreparer.PrepareAsync"/> returns it; null on failure.</param>
/// <param name="Error">Why it failed, on one line; null on success.</param>
public readonly record struct PrepareOutcome(string ActorOrModelName, string? ModelName, string? Error)
{
    public bool Succeeded => ModelName is not null;
}

/// <summary>
/// The offline half of the renderer, behind one seam so a host can choose how it runs.
/// </summary>
/// <remarks>
/// <para>
/// Preparation reads BFRES/BFSHA through ShaderLibrary, which carries its own vendored
/// BfresLibrary build and patches it at runtime. A host that already loads a different
/// BfresLibrary build into its process cannot also load that one - they share an assembly name.
/// Such a host uses <see cref="OutOfProcessPreparer"/>, which runs the preparer as a child
/// process. A host with no such clash can reference <c>WildRenderingSharp.Preparation</c> and
/// use its <c>InProcessPreparer</c>, which is faster to start and shares the process's mod state.
/// </para>
/// <para>Both are safe to call from a background thread and never touch GL.</para>
/// </remarks>
public interface IModelPreparer
{
    /// <summary>
    /// Builds the shared, model-independent assets the pipeline needs (system shaders, deferred
    /// materials, system textures, sky LUT, cloud/sky/lens-flare programs), skipping anything
    /// already present. Must have completed before a <see cref="Pipeline.DeferredPipeline"/> is constructed.
    /// </summary>
    Task EnsureSystemAssetsAsync(string romfsRoot, CacheLayout cache, Action<string>? log = null, CancellationToken cancellationToken = default);

    /// <summary>Prepares one actor or model, or confirms it is already up to date.</summary>
    /// <returns>The RESOLVED model name - what <see cref="Assets.ModelLoader"/> and <see cref="CacheLayout.ModelDirectory"/> take.</returns>
    Task<string> PrepareAsync(PrepareRequest request, Action<string>? log = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepares every name in <paramref name="request"/>, several at a time, building the system
    /// assets first. One failing - even one that takes its worker process down - fails only itself.
    /// </summary>
    /// <param name="onOutcome">Called once per name as it finishes, from a background thread, in no particular order.</param>
    /// <returns>Every outcome, once all have finished.</returns>
    Task<IReadOnlyList<PrepareOutcome>> PrepareManyAsync(PrepareBatchRequest request, Action<PrepareOutcome>? onOutcome = null,
        Action<string>? log = null, CancellationToken cancellationToken = default);
}
