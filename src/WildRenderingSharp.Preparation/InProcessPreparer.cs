using WildRenderingSharp.Hosting;
using WildRenderingSharp.Hosting.Preparers;
using WildRenderingSharp.Storage;

namespace WildRenderingSharp.Preparation;

/// <summary><see cref="IModelPreparer"/> on a background thread of this process, through <see cref="ModelPreparer"/>.</summary>
public sealed class InProcessPreparer : IModelPreparer
{
    static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task EnsureSystemAssetsAsync(string romfsRoot, CacheLayout cache, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() => ModelPreparer.EnsureSystemAssets(romfsRoot, cache, log), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<IReadOnlyList<PrepareOutcome>> PrepareManyAsync(PrepareBatchRequest request, Action<PrepareOutcome>? onOutcome = null,
        Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                ModelPreparer.EnsureSystemAssets(request.RomfsRoot, request.Cache, log);

                var outcomes = new System.Collections.Concurrent.ConcurrentBag<PrepareOutcome>();
                ModelPreparer.PrepareMany(request.RomfsRoot, request.ActorOrModelNames, request.Cache, request.EffectiveParallelism,
                    null, outcome => { outcomes.Add(outcome); onOutcome?.Invoke(outcome); },
                    request.ImportAnimations, request.Force, request.ModRomfsLayers, cancellationToken);
                return (IReadOnlyList<PrepareOutcome>)outcomes.ToList();
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<string> PrepareAsync(PrepareRequest request, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                return ModelPreparer.PrepareIfNeeded(request.RomfsRoot, request.ActorOrModelName, request.Cache,
                    log, request.ImportAnimations, request.Force, request.ModRomfsLayers);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }
}
