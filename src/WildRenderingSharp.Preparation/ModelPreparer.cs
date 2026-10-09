using System.Collections.Concurrent;
using ShaderLibrary.CompileTool;
using WildRenderingSharp.Hosting;
using WildRenderingSharp.Preparation.Totk;

namespace WildRenderingSharp.Preparation;

/// <summary>
/// The Tears of the Kingdom <c>--prepare</c> pipeline called in-process: the shared system assets every model needs, then a model's
/// geometry, textures, animations, material blocks and shader programs. Each call opens the romfs it is given, with any mod folders
/// layered over it, so nothing about the ROM outlives the call.
/// </summary>
public static class ModelPreparer
{
    static readonly object PatchGate = new();
    static bool _patched;

    /// <summary>Patches BfresLibrary once per process so it can parse TotK's V10 materials.</summary>
    public static void EnsureBfresPatched()
    {
        lock (PatchGate)
        {
            if (_patched)
                return;
            BfresLibraryPatches.EnsureApplied();
            _patched = true;
        }
    }

    public static void EnsureSystemAssets(string romfsRoot, CacheLayout cache, Action<string>? log = null)
    {
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
        {
            log?.Invoke($"[prepare] no romfs at '{romfsRoot}' - system assets not built.");
            return;
        }

        using var romfs = new TotkRomfs(romfsRoot);
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
        EnsureBfresPatched();
        using var romfs = new TotkRomfs(romfsRoot, modRomfsLayers);
        TotkStringTable.Select(romfs.Layered, cache);
        return PrepareIfNeeded(romfs, actorOrModelName, cache, log, importAnims, force);
    }

    public static void PrepareMany(string romfsRoot, IReadOnlyList<string> actorOrModelNames, CacheLayout cache, int parallelism,
        Action<string>? onBegin, Action<PrepareOutcome> onOutcome, bool importAnims = true, bool force = false,
        IEnumerable<string>? modRomfsLayers = null, CancellationToken cancellationToken = default)
    {
        EnsureBfresPatched();
        using var romfs = new TotkRomfs(romfsRoot, modRomfsLayers);
        TotkStringTable.Select(romfs.Layered, cache);
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

    /// <summary>False if the prepared model was built from different romfs files than the root and mod set would supply now.</summary>
    public static bool IsUpToDate(string romfsRoot, string dataDirectory, IEnumerable<string>? modRomfsLayers = null)
    {
        using var romfs = new TotkRomfs(romfsRoot, modRomfsLayers);
        return TotkSourceStamp.IsUpToDate(romfs, dataDirectory);
    }

    static string PrepareIfNeeded(TotkRomfs romfs, string actorOrModelName, CacheLayout cache, Action<string>? log, bool importAnims, bool force)
    {
        string modelName = TotkModelPreparer.ResolveModelName(romfs, actorOrModelName);
        if (!force && cache.IsPrepared(modelName) && TotkSourceStamp.IsUpToDate(romfs, cache.ModelDirectory(modelName)))
        {
            log?.Invoke($"[prepare] {modelName} is already prepared and up to date.");
            return modelName;
        }
        return TotkModelPreparer.Prepare(romfs, actorOrModelName, cache, log, importAnims);
    }

    static string OneLine(string message) => string.Join(' ', message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
}
