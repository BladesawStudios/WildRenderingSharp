using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Hosting.Preparers;

/// <summary>The offline half of the renderer, behind one seam so a host can choose how it runs. Safe to call from any thread; never touches GL.</summary>
public interface IModelPreparer
{
    /// <summary>Builds the assets every model of the game needs; <paramref name="game"/> is "botw" for Breath of the Wild and otherwise Tears of the Kingdom.</summary>
    Task EnsureSystemAssetsAsync(string romfsRoot, CacheLayout cache, Action<string>? log = null, CancellationToken cancellationToken = default, string? game = null);

    Task<string> PrepareAsync(PrepareRequest request, Action<string>? log = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PrepareOutcome>> PrepareManyAsync(PrepareBatchRequest request, Action<PrepareOutcome>? onOutcome = null,
        Action<string>? log = null, CancellationToken cancellationToken = default);
}
