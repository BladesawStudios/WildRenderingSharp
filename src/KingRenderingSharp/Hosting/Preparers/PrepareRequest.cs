using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Hosting.Preparers;

/// <summary>One request to prepare an actor or model into a <see cref="CacheLayout"/>.</summary>
public sealed record PrepareRequest(
    string RomfsRoot,
    string ActorOrModelName,
    CacheLayout Cache,
    IReadOnlyList<string>? ModRomfsLayers = null,
    bool ImportAnimations = true,
    bool Force = false,
    string? Game = null);
