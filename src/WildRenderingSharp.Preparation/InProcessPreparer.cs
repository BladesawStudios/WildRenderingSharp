using WildRenderingSharp.Hosting;

namespace WildRenderingSharp.Preparation;

/// <summary><see cref="IModelPreparer"/> on a background thread of this process, through <see cref="ModelPreparer"/>.</summary>
/// <remarks>
/// Only for hosts that do not load their own BfresLibrary build - this loads ShaderLibrary's. See
/// <see cref="IModelPreparer"/>'s remarks, and <see cref="OutOfProcessPreparer"/> otherwise.
/// Preparations are serialised: ShaderLibrary keeps process-wide state (the romfs overlay, its
/// string table, the overlay's file recorder) that two concurrent preparations would share.
/// </remarks>
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
                if (request.ModRomfsLayers is { } mods)
                    ModelPreparer.SetModRomfsLayers(mods);
                ModelPreparer.EnsureSystemAssets(request.RomfsRoot, request.Cache, log);

                var outcomes = new System.Collections.Concurrent.ConcurrentBag<PrepareOutcome>();
                ModelPreparer.PrepareMany(request.RomfsRoot, request.ActorOrModelNames, request.Cache, request.EffectiveParallelism,
                    null, outcome => { outcomes.Add(outcome); onOutcome?.Invoke(outcome); },
                    request.ImportAnimations, request.Force, cancellationToken);
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
                if (request.ModRomfsLayers is { } mods)
                    ModelPreparer.SetModRomfsLayers(mods);
                return ModelPreparer.PrepareIfNeeded(request.RomfsRoot, request.ActorOrModelName, request.Cache,
                    log, request.ImportAnimations, request.Force);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }
}
