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
