using KingRenderingSharp.Hosting;
using KingRenderingSharp.Hosting.Preparers;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Preparation;

/// <summary><see cref="IModelPreparer"/> on a background thread of this process, through <see cref="ModelPreparer"/>.</summary>
public sealed class InProcessPreparer : IModelPreparer
{
    static readonly SemaphoreSlim Gate = new(1, 1);

    // A model without mod layers is prepared by the pack tool when it is built beside this one; the older preparation reads the layered romfs.
    static bool UsesPackTool(PrepareRequest request) => (request.ModRomfsLayers?.Count ?? 0) == 0 && PackTool.Executable() is not null;

    public async Task EnsureSystemAssetsAsync(string romfsRoot, CacheLayout cache, Action<string>? log = null, CancellationToken cancellationToken = default, string? game = null)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                if (game == "botw") Botw.BotwSystemAssets.Ensure(Core.Rom.Games.BotwRom.Open(romfsRoot), cache, log);
                else ModelPreparer.EnsureSystemAssets(romfsRoot, cache, log);
            }, cancellationToken).ConfigureAwait(false);
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

                if ((request.ModRomfsLayers?.Count ?? 0) == 0 && PackTool.Executable() is not null)
                {
                    List<PrepareOutcome> prepared = [];
                    foreach (string name in request.ActorOrModelNames.Distinct(StringComparer.Ordinal))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        PrepareOutcome outcome;
                        try
                        {
                            outcome = new PrepareOutcome(name, PackTool.PrepareModel(request.RomfsRoot, name, request.Cache, request.ImportAnimations, request.Force, log), null);
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            outcome = new PrepareOutcome(name, null, exception.GetBaseException().Message.ReplaceLineEndings(" "));
                        }

                        prepared.Add(outcome);
                        onOutcome?.Invoke(outcome);
                    }

                    return (IReadOnlyList<PrepareOutcome>)prepared;
                }

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
                if (UsesPackTool(request))
                    return PackTool.PrepareModel(request.RomfsRoot, request.ActorOrModelName, request.Cache, request.ImportAnimations, request.Force, log);
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
