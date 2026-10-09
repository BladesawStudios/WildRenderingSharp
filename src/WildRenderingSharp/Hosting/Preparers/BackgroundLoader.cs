using System.Collections.Concurrent;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Hosting.Preparers;

/// <summary>What a loader job hands back: <paramref name="Apply"/> runs on the render thread if the job's owner is still current (true when it added something), otherwise <paramref name="Discard"/> gives back what the job made.</summary>
public sealed record LoaderResult(Func<bool> Apply, Action Discard);

/// <summary>
/// Runs a host's model loads on a thread of its own with a GL context sharing the host's, so a job does its buffers, textures and
/// programs there and hands back a step for the render thread to make the vertex arrays. A result is applied only if its owner is
/// still the one passed to ApplyFinished.
/// </summary>
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

    // createContext makes the loader's context on the render thread (null keeps loading there), and failed gives the result for a job that threw.
    public BackgroundLoader(GL gl, Func<IGLContext?> createContext, Func<Exception, LoaderResult> failed, Action<string>? log = null)
    {
        _gl = gl;
        _createContext = createContext;
        _failed = failed;
        _log = log;
    }

    // Starts the thread once; false when the host gave it no context.
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

                // Work in a shared context is not visible to another until it is flushed.
                _gl.Finish();
                _finished.Enqueue((job, result));
            }
        }
        _context.Clear();
    }

    // Applies what the loader has finished, on the render thread. True when anything was added to the scene.
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

    // Drops queued jobs and waits for the one in flight, before an owner goes away.
    public void WaitIdle()
    {
        Interlocked.Increment(ref _generation);
        while (_jobs.TryTake(out _)) { }
        lock (_busy) { }
    }

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
