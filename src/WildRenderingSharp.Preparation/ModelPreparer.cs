using System.Collections.Concurrent;
using WildRenderingSharp.Hosting;
using WildRenderingSharp.Hosting.Preparers;
using WildRenderingSharp.Preparation.Totk;
using WildRenderingSharp.Storage;

namespace WildRenderingSharp.Preparation;

/// <summary>Exports TotK assets to a render-ready format. Allows for layered mod folders overriding RomFS.</summary>
public static class ModelPreparer
{
    public static void EnsureSystemAssets(string romfsRoot, CacheLayout cache, Action<string>? log = null)
    {
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
        {
            log?.Invoke($"[prepare] no romfs at '{romfsRoot}' - system assets not built.");
            return;
        }

        using var romfs = new TotkRomfs(romfsRoot);
        using var strings = ExternalStringTable.Use(romfs.Base);
        TotkSystemAssets.Ensure(romfs, cache, log);
    }

    public static string ResolveModelName(string romfsRoot, string actorOrModelName, IEnumerable<string>? modRomfsLayers = null)
    {
        using var romfs = new TotkRomfs(romfsRoot, modRomfsLayers);
        return TotkModelPreparer.ResolveModelName(romfs, actorOrModelName);
    }

    public static string PrepareIfNeeded(string romfsRoot, string actorOrModelName, CacheLayout cache, Action<string>? log = null,
        bool importAnims = true, bool force = false, IEnumerable<string>? modRomfsLayers = null)
    {
        using var romfs = new TotkRomfs(romfsRoot, modRomfsLayers);
        using var strings = ExternalStringTable.Use(romfs.Layered);
        return PrepareIfNeeded(romfs, actorOrModelName, cache, log, importAnims, force);
    }

    public static void PrepareMany(string romfsRoot, IReadOnlyList<string> actorOrModelNames, CacheLayout cache, int parallelism,
        Action<string>? onBegin, Action<PrepareOutcome> onOutcome, bool importAnims = true, bool force = false,
        IEnumerable<string>? modRomfsLayers = null, CancellationToken cancellationToken = default)
    {
        using var romfs = new TotkRomfs(romfsRoot, modRomfsLayers);
        using var strings = ExternalStringTable.Use(romfs.Layered);
        TotkShaderArchives.Extract(romfs.Base, cache, "material");

        var modelGates = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallelism), CancellationToken = cancellationToken };
        Parallel.ForEach(actorOrModelNames, options, name =>
        {
            onBegin?.Invoke(name);
            try
            {
                string model = TotkModelPreparer.ResolveModelName(romfs, name);
                lock (modelGates.GetOrAdd(model, _ => new object()))
                    model = PrepareIfNeeded(romfs, name, cache, null, importAnims, force);
                onOutcome(new PrepareOutcome(name, model, null));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                onOutcome(new PrepareOutcome(name, null, OneLine(ex.GetBaseException().Message)));
            }
        });
    }

    public static bool IsUpToDate(string romfsRoot, string dataDirectory, IEnumerable<string>? modRomfsLayers = null)
    {
        using var romfs = new TotkRomfs(romfsRoot, modRomfsLayers);
        return TotkSourceStamp.IsUpToDate(romfs, dataDirectory);
    }

    static string PrepareIfNeeded(TotkRomfs romfs, string actorOrModelName, CacheLayout cache, Action<string>? log, bool importAnims, bool force)
    {
        string modelName = TotkModelPreparer.ResolveModelName(romfs, actorOrModelName);
        var stages = force || !cache.IsPrepared(modelName)
            ? TotkSourceStamp.Stages.All
            : TotkSourceStamp.Stale(romfs, cache.ModelDirectory(modelName), importAnims);
        if (stages == TotkSourceStamp.Stages.None)
        {
            log?.Invoke($"[prepare] {modelName} is already prepared and up to date.");
            return modelName;
        }
        string directory = cache.ModelDirectory(modelName);
        if (stages == TotkSourceStamp.Stages.Pack)
        {
            // Exported before files were packed: compress them in place, which reads and writes only those files.
            CacheFiles.Pack(directory);
            TotkSourceStamp.MarkPacked(directory);
            log?.Invoke($"[prepare] {modelName}: packed.");
            return modelName;
        }
        if (stages != TotkSourceStamp.Stages.All)
            log?.Invoke($"[prepare] {modelName}: redoing only {stages}.");
        return TotkModelPreparer.Prepare(romfs, actorOrModelName, cache, log, importAnims, stages);
    }

    static string OneLine(string message) => string.Join(' ', message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
}
