using WildRenderingSharp.Storage;

namespace WildRenderingSharp.Hosting.Preparers;

/// <summary>
/// Runs <c>WildRenderingSharp.Preparation</c> as a child process - see <see cref="IModelPreparer"/> for when a host needs this
/// rather than preparing in-process.
/// </summary>
public sealed class OutOfProcessPreparer : IModelPreparer
{
    public const string ResultPrefix = PreparerProtocol.ResultPrefix;

    const int FailureTailLines = 40;

    readonly PreparerProcess _process;

    public OutOfProcessPreparer(string? executablePath = null)
    {
        ExecutablePath = executablePath ?? DefaultExecutablePath();
        _process = new PreparerProcess(ExecutablePath);
    }

    public string ExecutablePath { get; }

    public bool IsAvailable => _process.IsAvailable;

    public static string DefaultExecutablePath()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "wrs-prepare");
        string exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "WildRenderingSharp.Preparation.exe" : "WildRenderingSharp.Preparation");
        // Framework-dependent builds always have the .dll; the native apphost may be missing on some platforms.
        return File.Exists(exe) ? exe : Path.Combine(dir, "WildRenderingSharp.Preparation.dll");
    }

    public async Task EnsureSystemAssetsAsync(string romfsRoot, CacheLayout cache, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        await RunForResultAsync(PreparerProtocol.EnsureSystem(romfsRoot, cache.Root), log, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> PrepareAsync(PrepareRequest request, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        string? result = await RunForResultAsync(PreparerProtocol.Prepare(request), log, cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException($"The preparer finished without naming the model it prepared for '{request.ActorOrModelName}'.");
    }

    public Task<IReadOnlyList<PrepareOutcome>> PrepareManyAsync(PrepareBatchRequest request, Action<PrepareOutcome>? onOutcome = null,
        Action<string>? log = null, CancellationToken cancellationToken = default) =>
        new BatchPreparation(_process, request, onOutcome, log).RunAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> PrepareBakeAsync(string romfsRoot, CacheLayout cache, IEnumerable<string> tiles,
        Action<string>? onTileDone = null, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        var failed = new List<string>();
        string listFile = Path.Combine(Path.GetTempPath(), $"wrs-bake-{Environment.ProcessId}-{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(listFile, tiles.Distinct(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
        try
        {
            int exitCode = await _process.RunAsync(PreparerProtocol.PrepareBake(romfsRoot, cache.Root, listFile), line =>
            {
                if (PreparerProtocol.TrySplit(line, PreparerProtocol.DonePrefix, out string tile, out _))
                {
                    onTileDone?.Invoke(tile);
                }
                else if (PreparerProtocol.TrySplit(line, PreparerProtocol.FailPrefix, out tile, out string error))
                {
                    lock (failed)
                        failed.Add(tile);
                    log?.Invoke($"bake {tile}: {error}");
                }
                else if (!line.StartsWith(PreparerProtocol.BeginPrefix, StringComparison.Ordinal))
                {
                    log?.Invoke(line);
                }
            }, cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
                log?.Invoke($"the bake preparer exited with code {exitCode}");
        }
        finally
        {
            try { File.Delete(listFile); } catch { /* temp file */ }
        }
        return failed;
    }

    // Runs a command whose output is progress, with the model it names on a result line; a nonzero exit throws with the output's tail.
    async Task<string?> RunForResultAsync(IReadOnlyList<string> arguments, Action<string>? log, CancellationToken cancellationToken)
    {
        string? result = null;
        var tail = new Queue<string>();
        int exitCode = await _process.RunAsync(arguments, line =>
        {
            if (line.StartsWith(ResultPrefix, StringComparison.Ordinal))
            {
                result = line[ResultPrefix.Length..].Trim();
                return;
            }
            lock (tail)
            {
                tail.Enqueue(line);
                while (tail.Count > FailureTailLines)
                    tail.Dequeue();
            }
            log?.Invoke(line);
        }, cancellationToken).ConfigureAwait(false);

        if (exitCode == 0)
            return result;
        string detail;
        lock (tail)
            detail = string.Join(Environment.NewLine, tail);
        throw new InvalidOperationException($"The model preparer failed (exit code {exitCode}):{Environment.NewLine}{detail}");
    }
}
