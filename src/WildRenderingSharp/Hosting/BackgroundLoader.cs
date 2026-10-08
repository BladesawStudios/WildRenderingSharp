using System.Collections.Concurrent;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Hosting;

/// <summary>What a loader job hands back to the render thread.</summary>
/// <param name="Apply">Runs on the render thread if the job's owner is still the current one. True when it added something to the scene.</param>
/// <param name="Discard">Runs on the render thread otherwise, giving back what the job made.</param>
public sealed record LoaderResult(Func<bool> Apply, Action Discard);

/// <summary>
/// Runs a host's model loads on a thread of its own, with a GL context that shares the host's, so neither the view nor its UI waits on a model.
/// </summary>
/// <remarks>
/// <para>
/// Buffers, textures and programs are shared between contexts, so a load does all of that on the loader; vertex arrays are not, so each job
/// hands back a small step for the render thread (<see cref="LoaderResult.Apply"/>) that makes those.
/// </para>
/// <para>
/// A job belongs to an owner (the world it was queued for) and a generation. Its result is applied only if that owner is still the one passed to
/// <see cref="ApplyFinished"/> and the generation has not moved on, and is discarded otherwise. An owner is only disposed while the loader is
/// between jobs (<see cref="WaitIdle"/>), since a load uses the owner's program cache.
/// </para>
/// </remarks>
public sealed class BackgroundLoader : IDisposable
{
    sealed record Job(object Owner, int Generation, Func<LoaderResult> Run);

    readonly GL _gl;
    readonly Func<IGLContext?> _createContext;
    readonly Func<Exception, LoaderResult> _failed;
    readonly Action<string>? _log;
    readonly BlockingCollection<Job> _jobs = new();
    readonly ConcurrentQueue<(Job Job, LoaderResult Result)> _finished = new();
    readonly object _busy = new();

    Thread? _thread;
    IGLContext? _context;
    bool _unavailable;
    int _generation;

    /// <param name="createContext">Makes the loader's context on the render thread: a hidden window sharing the host's. Null keeps loading on the render thread.</param>
    /// <param name="failed">The result for a job that threw, the host's accounting of a model that could not be loaded.</param>
    public BackgroundLoader(GL gl, Func<IGLContext?> createContext, Func<Exception, LoaderResult> failed, Action<string>? log = null)
    {
        _gl = gl;
        _createContext = createContext;
        _failed = failed;
        _log = log;
    }

    /// <summary>Starts the thread once. False when the host could not give it a context, and loading stays on the render thread.</summary>
    public bool EnsureStarted()
    {
        if (_thread is not null)
            return true;
        if (_unavailable)
            return false;

        try
        {
            _context = _createContext();
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Loader context: {ex.Message}");
        }
        if (_context is null)
        {
            _unavailable = true;
            return false;
        }

        _thread = new Thread(Loop) { IsBackground = true, Name = "Model loader", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
        return true;
    }

    public bool Started => _thread is not null;

    /// <summary>Queues a job for <paramref name="owner"/>. It runs on the loader thread.</summary>
    public void Queue(object owner, Func<LoaderResult> run) =>
        _jobs.Add(new Job(owner, Volatile.Read(ref _generation), run));

    void Loop()
    {
        _context!.MakeCurrent();
        foreach (Job job in _jobs.GetConsumingEnumerable())
        {
            lock (_busy)
            {
                if (job.Generation != Volatile.Read(ref _generation))
                    continue;

                LoaderResult result;
                try
                {
                    result = job.Run();
                }
                catch (Exception ex)
                {
                    result = _failed(ex);
                }

                // A shared context's work is not visible to another until it is flushed, and a texture sampled before its upload has landed reads
                // back as whatever the memory held.
                _gl.Finish();
                _finished.Enqueue((job, result));
            }
        }
        _context.Clear();
    }

    /// <summary>Applies what the loader has finished, on the render thread. True when anything was added to the scene.</summary>
    /// <param name="currentOwner">The owner whose results still count.</param>
    /// <param name="applyFailed">Told of an <see cref="LoaderResult.Apply"/> that threw.</param>
    public bool ApplyFinished(object? currentOwner, Action<Exception>? applyFailed = null)
    {
        bool added = false;
        while (_finished.TryDequeue(out var item))
        {
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
        return added;
    }

    /// <summary>Drops queued jobs and waits for the one in flight, before an owner goes away. Results already finished are kept until <see cref="DropFinished"/>.</summary>
    public void WaitIdle()
    {
        Interlocked.Increment(ref _generation);
        while (_jobs.TryTake(out _)) { }
        lock (_busy) { }
    }

    /// <summary>As <see cref="WaitIdle"/>, then gives back everything the loader had finished and nothing has applied.</summary>
    public void DropFinished()
    {
        WaitIdle();
        while (_finished.TryDequeue(out var item))
            item.Result.Discard();
    }

    public void Dispose()
    {
        WaitIdle();
        _jobs.CompleteAdding();
        _thread?.Join(2000);
        _thread = null;
    }
}
