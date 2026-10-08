namespace WildRenderingSharp.Hosting;

/// <summary>The offline half of the renderer, behind one seam so a host can choose how it runs.</summary>
public interface IModelPreparer
{
    Task EnsureSystemAssetsAsync(string romfsRoot, CacheLayout cache, Action<string>? log = null, CancellationToken cancellationToken = default);

    Task<string> PrepareAsync(PrepareRequest request, Action<string>? log = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PrepareOutcome>> PrepareManyAsync(PrepareBatchRequest request, Action<PrepareOutcome>? onOutcome = null,
        Action<string>? log = null, CancellationToken cancellationToken = default);
}
