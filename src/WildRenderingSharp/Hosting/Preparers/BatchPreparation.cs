namespace WildRenderingSharp.Hosting.Preparers;

/// <summary>
/// Prepares a batch of models in preparer processes, and isolates a crash: when a process dies partway, the models it had begun but not
/// finished are retried one at a time, so the one that crashed it is named and the rest are not lost.
/// </summary>
sealed class BatchPreparation(PreparerProcess process, PrepareBatchRequest request, Action<PrepareOutcome>? onOutcome, Action<string>? log)
{
    const int TailLines = 20;

    readonly Dictionary<string, PrepareOutcome> _outcomes = new(StringComparer.Ordinal);

    // What a process printed that was not progress, and which models it had begun.
    sealed record Round(HashSet<string> Begun, Queue<string> Tail)
    {
        public int ExitCode { get; set; }
    }

    public async Task<IReadOnlyList<PrepareOutcome>> RunAsync(CancellationToken cancellationToken)
    {
        var all = request.ActorOrModelNames.Distinct(StringComparer.Ordinal).ToList();
        var pending = new Queue<(List<string> Names, int Jobs)>();
        pending.Enqueue((all, request.EffectiveParallelism));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (queued, jobs) = pending.Dequeue();
            var names = Unfinished(queued);
            if (names.Count == 0)
                continue;

            var round = await RunRoundAsync(names, jobs, cancellationToken).ConfigureAwait(false);
            Triage(names, jobs, round, pending);
        }

        lock (_outcomes)
            return [.. all.Select(n => _outcomes[n])];
    }

    List<string> Unfinished(IEnumerable<string> names)
    {
        lock (_outcomes)
            return names.Where(n => !_outcomes.ContainsKey(n)).ToList();
    }

    void Finish(PrepareOutcome outcome)
    {
        lock (_outcomes)
        {
            if (!_outcomes.TryAdd(outcome.ActorOrModelName, outcome))
                return;
        }
        onOutcome?.Invoke(outcome);
    }

    async Task<Round> RunRoundAsync(List<string> names, int jobs, CancellationToken cancellationToken)
    {
        var round = new Round(new HashSet<string>(StringComparer.Ordinal), new Queue<string>());
        string listFile = Path.Combine(Path.GetTempPath(), $"wrs-batch-{Environment.ProcessId}-{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(listFile, names, cancellationToken).ConfigureAwait(false);
        try
        {
            round.ExitCode = await process.RunAsync(PreparerProtocol.PrepareBatch(request, listFile, jobs), line => OnLine(line, round), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(listFile); } catch { /* temp file */ }
        }
        return round;
    }

    void OnLine(string line, Round round)
    {
        if (line.StartsWith(PreparerProtocol.BeginPrefix, StringComparison.Ordinal))
        {
            lock (round.Begun)
                round.Begun.Add(line[PreparerProtocol.BeginPrefix.Length..]);
        }
        else if (PreparerProtocol.TrySplit(line, PreparerProtocol.DonePrefix, out string name, out string model))
        {
            Finish(new PrepareOutcome(name, model, null));
        }
        else if (PreparerProtocol.TrySplit(line, PreparerProtocol.FailPrefix, out name, out string error))
        {
            Finish(new PrepareOutcome(name, null, error));
        }
        else
        {
            lock (round.Tail)
            {
                round.Tail.Enqueue(line);
                while (round.Tail.Count > TailLines)
                    round.Tail.Dequeue();
            }
            log?.Invoke(line);
        }
    }

    // Decides what becomes of the models a process left unfinished.
    void Triage(List<string> names, int jobs, Round round, Queue<(List<string> Names, int Jobs)> pending)
    {
        var unfinished = Unfinished(names);
        if (unfinished.Count == 0)
            return;

        List<string> inFlight;
        lock (round.Begun)
            inFlight = unfinished.Where(round.Begun.Contains).ToList();
        var notBegun = unfinished.Except(inFlight, StringComparer.Ordinal).ToList();

        if (inFlight.Count == 0)
        {
            // It died before starting on any of what is left - the romfs, the cache or the system assets, not a model - so running it again would only do the same.
            string detail;
            lock (round.Tail)
                detail = round.Tail.LastOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
            foreach (string name in notBegun)
                Finish(new PrepareOutcome(name, null, $"the preparer exited with code {round.ExitCode} before reaching it. {detail}".Trim()));
            return;
        }

        if (jobs == 1 && names.Count == 1)
        {
            // Alone in its own worker, so the crash was its own.
            Finish(new PrepareOutcome(names[0], null, $"the preparer crashed on it (exit code {round.ExitCode})"));
            return;
        }
        foreach (string suspect in inFlight)
            pending.Enqueue(([suspect], 1));
        if (notBegun.Count > 0)
            pending.Enqueue((notBegun, jobs));
    }
}
