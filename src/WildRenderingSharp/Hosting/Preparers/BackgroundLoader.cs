using System.Collections.Concurrent;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Hosting.Preparers;

/// <summary>
/// What a loader job hands back: <paramref name="Apply"/> runs on the render thread if the job's owner is still current (true when it added
/// something), otherwise <paramref name="Discard"/> gives back what the job made.
/// </summary>
public sealed record LoaderResult(Func<bool> Apply, Action Discard);

/// <summary>
/// Runs a host's model loads on threads with GL contexts sharing the host's, and hands back a step for the render thread to finish. A
/// result is applied only if its owner is still the one passed to ApplyFinished.
/// </summary>
/// <remarks>
/// Jobs are taken in the order they were queued, by as many workers as the host gave contexts to, so several load at once. Results are
/// applied in that same order however they finished: a job that finishes early waits for the ones queued before it, so what the scene
/// holds, and the order it holds it in, is the same on every run. A job the owner's change made stale is dropped and counts as finished
/// with nothing to apply, so the ones behind it are never held up by it.
/// </remarks>
public sealed class BackgroundLoader : IDisposable
{
    sealed record Job(object Owner, int Generation, long Order, Func<LoaderResult> Run);

    readonly GL _gl;
    readonly Func<int, IGLContext?> _createContext;
    readonly Func<Exception, LoaderResult> _failed;
    readonly Action<string>? _log;
    readonly int _workers;
    readonly BlockingCollection<Job> _jobs = new();

    // Finished jobs by the order they were queued in; a null result is a job dropped without running.
    readonly ConcurrentDictionary<long, (Job Job, LoaderResult? Result)> _finished = new();
    readonly object _applying = new();
    readonly List<(Thread Thread, IGLContext Context, object Busy)> _threads = [];
    bool _started, _unavailable;
    long _queued;
    long _nextToApply;
    int _generation;

    // createContext makes the context for worker i on the render thread (null keeps loading there), and failed gives the result for a job that threw.
    public BackgroundLoader(GL gl, Func<int, IGLContext?> createContext, Func<Exception, LoaderResult> failed, Action<string>? log = null, int workers = 1)
    {
        _gl = gl;
        _createContext = createContext;
        _failed = failed;
        _log = log;
        _workers = Math.Max(1, workers);
    }

    // Starts the threads once; false when the host gave it no context at all. A host that cannot make as many as asked for gets the ones it could.
    public bool EnsureStarted()
    {
        if (_started)
            return true;
        if (_unavailable)
            return false;

        for (int i = 0; i < _workers; i++)
        {
            IGLContext? context = null;
            try
            {
                context = _createContext(i);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Loader context {i}: {ex.Message}");
            }
            if (context is null)
                break;

            var busy = new object();
            var thread = new Thread(() => Loop(context, busy)) { IsBackground = true, Name = $"Model loader {i}", Priority = ThreadPriority.BelowNormal };
            _threads.Add((thread, context, busy));
        }

        if (_threads.Count == 0)
        {
            _unavailable = true;
            return false;
        }

        foreach (var (thread, _, _) in _threads)
            thread.Start();
        _started = true;
        return true;
    }

    public bool Started => _started;

    public void Queue(object owner, Func<LoaderResult> run) =>
        _jobs.Add(new Job(owner, Volatile.Read(ref _generation), Interlocked.Increment(ref _queued) - 1, run));

    void Loop(IGLContext context, object busy)
    {
        context.MakeCurrent();
        foreach (Job job in _jobs.GetConsumingEnumerable())
        {
            lock (busy)
            {
                if (job.Generation != Volatile.Read(ref _generation))
                {
                    _finished[job.Order] = (job, null);
                    continue;
                }

                LoaderResult result;
                try
                {
                    result = job.Run();
                }
                catch (Exception ex)
                {
                    result = _failed(ex);
                }

                // Work in a shared context is not visible to another until it is flushed.
                _gl.Finish();
                _finished[job.Order] = (job, result);
            }
        }
        context.Clear();
    }

    // Applies what the loaders have finished, in the order the jobs were queued, on the render thread. True when anything was added to the scene.
    public bool ApplyFinished(object? currentOwner, Action<Exception>? applyFailed = null)
    {
        bool added = false;
        lock (_applying)
        {
            while (_finished.TryRemove(_nextToApply, out var item))
            {
                _nextToApply++;
                if (item.Result is null)
                    continue;

                if (item.Job.Generation != Volatile.Read(ref _generation) || !ReferenceEquals(item.Job.Owner, currentOwner))
                {
                    item.Result.Discard();
                    continue;
                }

                try
                {
                    if (item.Result.Apply())
                        added = true;
                }
                catch (Exception ex)
                {
                    applyFailed?.Invoke(ex);
                }
            }
        }
        return added;
    }

    // Drops queued jobs and waits for the ones in flight, before an owner goes away.
    public void WaitIdle()
    {
        Interlocked.Increment(ref _generation);
        while (_jobs.TryTake(out Job? dropped))
            _finished[dropped.Order] = (dropped, null);
        foreach (var (_, _, busy) in _threads)
            lock (busy) { }
    }

    public void DropFinished()
    {
        WaitIdle();
        lock (_applying)
        {
            while (_finished.TryRemove(_nextToApply, out var item))
            {
                _nextToApply++;
                item.Result?.Discard();
            }
        }
    }

    public void Dispose()
    {
        WaitIdle();
        _jobs.CompleteAdding();
        foreach (var (thread, _, _) in _threads)
            thread.Join(2000);
        _threads.Clear();
        _started = false;
    }
}
