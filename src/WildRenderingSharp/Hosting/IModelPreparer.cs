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
}
