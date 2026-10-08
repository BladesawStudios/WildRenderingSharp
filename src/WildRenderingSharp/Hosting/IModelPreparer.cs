namespace WildRenderingSharp.Hosting;

/// <summary>The offline half of the renderer, behind one seam so a host can choose how it runs.</summary>
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
    /// Builds the shared, model-independent assets the pipeline needs (system shaders, deferred materials, system textures, sky
    /// LUT, cloud/sky/lens-flare programs), skipping anything already present. Must have completed before a <see
    /// cref="Pipeline.DeferredPipeline"/> is constructed.
    /// </summary>
    Task EnsureSystemAssetsAsync(string romfsRoot, CacheLayout cache, Action<string>? log = null, CancellationToken cancellationToken = default);

    /// <summary>Prepares one actor or model, or confirms it is already up to date.</summary>
    Task<string> PrepareAsync(PrepareRequest request, Action<string>? log = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepares every name in <paramref name="request"/>, several at a time, building the system assets first. One failing - even
    /// one that takes its worker process down - fails only itself.
    /// </summary>
    Task<IReadOnlyList<PrepareOutcome>> PrepareManyAsync(PrepareBatchRequest request, Action<PrepareOutcome>? onOutcome = null,
        Action<string>? log = null, CancellationToken cancellationToken = default);
}
