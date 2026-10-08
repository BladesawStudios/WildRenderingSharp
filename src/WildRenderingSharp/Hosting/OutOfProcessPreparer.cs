using System.Diagnostics;
using System.Text;

namespace WildRenderingSharp.Hosting;

/// <summary>
/// Runs <c>WildRenderingSharp.Preparation</c> as a child process - see <see cref="IModelPreparer"/> for when a host needs this
/// rather than preparing in-process.
/// </summary>
public sealed class OutOfProcessPreparer : IModelPreparer
{
    public const string ResultPrefix = "WRS_RESULT ";

    public string ExecutablePath { get; }

    public OutOfProcessPreparer(string? executablePath = null)
    {
        ExecutablePath = executablePath ?? DefaultExecutablePath();
    }

    public static string DefaultExecutablePath()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "wrs-prepare");
        string exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "WildRenderingSharp.Preparation.exe" : "WildRenderingSharp.Preparation");
        // Framework-dependent builds always have the .dll; the native apphost may be missing on
        // some platforms, in which case the dll is run through the dotnet host instead.
        return File.Exists(exe) ? exe : Path.Combine(dir, "WildRenderingSharp.Preparation.dll");
    }

    public bool IsAvailable => File.Exists(ExecutablePath);

    public async Task EnsureSystemAssetsAsync(string romfsRoot, CacheLayout cache, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        await RunAsync(["ensure-system", "--romfs", romfsRoot, "--cache", cache.Root], log, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> PrepareAsync(PrepareRequest request, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        var args = new List<string>
        {
            "prepare",
            "--romfs", request.RomfsRoot,
            "--actor", request.ActorOrModelName,
            "--cache", request.Cache.Root,
        };
        foreach (string mod in request.ModRomfsLayers ?? [])
        {
            args.Add("--mod");
            args.Add(mod);
        }
        if (!request.ImportAnimations)
            args.Add("--no-anims");
        if (request.Force)
            args.Add("--force");

        string? result = await RunAsync(args, log, cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException($"The preparer finished without naming the model it prepared for '{request.ActorOrModelName}'.");
    }

    public async Task<IReadOnlyList<PrepareOutcome>> PrepareManyAsync(PrepareBatchRequest request, Action<PrepareOutcome>? onOutcome = null,
        Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        var outcomes = new Dictionary<string, PrepareOutcome>(StringComparer.Ordinal);
        void Finish(PrepareOutcome outcome)
        {
            lock (outcomes)
            {
                if (!outcomes.TryAdd(outcome.ActorOrModelName, outcome))
                    return;
            }
            onOutcome?.Invoke(outcome);
        }

        var pending = new Queue<(List<string> Names, int Jobs)>();
        pending.Enqueue((request.ActorOrModelNames.Distinct(StringComparer.Ordinal).ToList(), request.EffectiveParallelism));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (names, jobs) = pending.Dequeue();
            names = names.Where(n => { lock (outcomes) return !outcomes.ContainsKey(n); }).ToList();
            if (names.Count == 0)
                continue;

            var begun = new HashSet<string>(StringComparer.Ordinal);
            var tail = new Queue<string>();
            string listFile = Path.Combine(Path.GetTempPath(), $"wrs-batch-{Environment.ProcessId}-{Guid.NewGuid():N}.txt");
            await File.WriteAllLinesAsync(listFile, names, cancellationToken).ConfigureAwait(false);
            int exitCode;
            try
            {
                var args = new List<string>
                {
                    "prepare-batch", "--romfs", request.RomfsRoot, "--cache", request.Cache.Root,
                    "--list", listFile, "--jobs", jobs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                };
                foreach (string mod in request.ModRomfsLayers ?? [])
                {
                    args.Add("--mod");
                    args.Add(mod);
                }
                if (!request.ImportAnimations)
                    args.Add("--no-anims");
                if (request.Force)
                    args.Add("--force");

                exitCode = await RunProcessAsync(args, line =>
                {
                    if (line.StartsWith("WRS_BEGIN ", StringComparison.Ordinal))
                    {
                        lock (begun)
                            begun.Add(line["WRS_BEGIN ".Length..]);
                    }
                    else if (TrySplit(line, "WRS_DONE ", out string name, out string model))
                        Finish(new PrepareOutcome(name, model, null));
                    else if (TrySplit(line, "WRS_FAIL ", out name, out string error))
                        Finish(new PrepareOutcome(name, null, error));
                    else
                    {
                        lock (tail)
                        {
                            tail.Enqueue(line);
                            while (tail.Count > 20)
                                tail.Dequeue();
                        }
                        log?.Invoke(line);
                    }
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                try { File.Delete(listFile); } catch { /* temp file */ }
            }

            var unfinished = names.Where(n => { lock (outcomes) return !outcomes.ContainsKey(n); }).ToList();
            if (unfinished.Count == 0)
                continue;

            List<string> inFlight;
            lock (begun)
                inFlight = unfinished.Where(begun.Contains).ToList();
            var notBegun = unfinished.Except(inFlight, StringComparer.Ordinal).ToList();

            if (inFlight.Count == 0)
            {
                // It died before starting on any of what is left - the romfs, the cache or the
                // system assets, not a model - so running it again would only do the same.
                string detail;
                lock (tail)
                    detail = tail.LastOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
                foreach (string name in notBegun)
                    Finish(new PrepareOutcome(name, null, $"the preparer exited with code {exitCode} before reaching it. {detail}".Trim()));
                continue;
            }

            if (jobs == 1 && names.Count == 1)
            {
                // Alone in its own worker, so the crash was its own.
                Finish(new PrepareOutcome(names[0], null, $"the preparer crashed on it (exit code {exitCode})"));
                continue;
            }
            foreach (string suspect in inFlight)
                pending.Enqueue(([suspect], 1));
            if (notBegun.Count > 0)
                pending.Enqueue((notBegun, jobs));
        }

        lock (outcomes)
            return [.. request.ActorOrModelNames.Distinct(StringComparer.Ordinal).Select(n => outcomes[n])];
    }

    public async Task<IReadOnlyList<string>> PrepareBakeAsync(string romfsRoot, CacheLayout cache, IEnumerable<string> tiles,
        Action<string>? onTileDone = null, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        var failed = new List<string>();
        string listFile = Path.Combine(Path.GetTempPath(), $"wrs-bake-{Environment.ProcessId}-{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(listFile, tiles.Distinct(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
        try
        {
            int exitCode = await RunProcessAsync(["prepare-bake", "--romfs", romfsRoot, "--cache", cache.Root, "--list", listFile], line =>
            {
                if (TrySplit(line, "WRS_DONE ", out string tile, out _))
                    onTileDone?.Invoke(tile);
                else if (TrySplit(line, "WRS_FAIL ", out tile, out string error))
                {
                    lock (failed)
                        failed.Add(tile);
                    log?.Invoke($"bake {tile}: {error}");
                }
                else if (!line.StartsWith("WRS_BEGIN ", StringComparison.Ordinal))
                    log?.Invoke(line);
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

    static bool TrySplit(string line, string prefix, out string name, out string value)
    {
        name = value = "";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        string rest = line[prefix.Length..];
        int tab = rest.IndexOf('\t');
        if (tab < 0)
            return false;
        name = rest[..tab];
        value = rest[(tab + 1)..];
        return true;
    }

    /// <summary>Preparing a map runs a dozen of these at once; at the host's own priority they take the cores its render thread needs.</summary>
    static void BelowNormal(Process process)
    {
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch { /* already gone, or not ours to change */ }
    }

    async Task<int> RunProcessAsync(IReadOnlyList<string> arguments, Action<string> onLine, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = StartInfo(arguments), EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is { } line) onLine(line); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is { } line) onLine(line); };
        process.Start();
        BelowNormal(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }
        process.WaitForExit();
        return process.ExitCode;
    }

    ProcessStartInfo StartInfo(IReadOnlyList<string> arguments)
    {
        if (!IsAvailable)
            throw new FileNotFoundException(
                $"The model preparer was not found at '{ExecutablePath}'. Hosts get it by importing build/WildRenderingSharp.targets.",
                ExecutablePath);

        var info = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(ExecutablePath)!,
        };
        if (ExecutablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            info.FileName = "dotnet";
            info.ArgumentList.Add(ExecutablePath);
        }
        else
        {
            info.FileName = ExecutablePath;
        }
        foreach (string a in arguments)
            info.ArgumentList.Add(a);
        return info;
    }

    async Task<string?> RunAsync(IReadOnlyList<string> arguments, Action<string>? log, CancellationToken cancellationToken)
    {
        var info = StartInfo(arguments);
        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        string? result = null;
        var tail = new Queue<string>();
        void Keep(string line)
        {
            lock (tail)
            {
                tail.Enqueue(line);
                while (tail.Count > 40)
                    tail.Dequeue();
            }
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not { } line)
                return;
            if (line.StartsWith(ResultPrefix, StringComparison.Ordinal))
            {
                result = line[ResultPrefix.Length..].Trim();
                return;
            }
            Keep(line);
            log?.Invoke(line);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not { } line)
                return;
            Keep(line);
            log?.Invoke(line);
        };

        process.Start();
        BelowNormal(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }
        // The parameterless wait drains the redirected streams, so the result line cannot be lost
        // to a race between process exit and the last OutputDataReceived.
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            string detail;
            lock (tail)
                detail = string.Join(Environment.NewLine, tail);
            throw new InvalidOperationException($"The model preparer failed (exit code {process.ExitCode}):{Environment.NewLine}{detail}");
        }
        return result;
    }
}
