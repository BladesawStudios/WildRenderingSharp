using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// How long each pass of a frame took on the GPU, from timestamp queries written between passes
/// and read back a few frames later, so measuring never waits on the card.
/// </summary>
/// <remarks>
/// A pass is timed from the previous mark to its own, so whatever ran between two named passes is
/// charged to the second. Marks are written where the pipeline already names its passes
/// (<see cref="GLDiagnostics.CheckPass"/>), and a pass that does not run in a frame simply has no row.
/// </remarks>
public sealed class GpuPassTimer : IDisposable
{
    const int Latency = 4;
    readonly GL _gl;
    readonly List<(string Name, uint Query)>[] _frames = new List<(string, uint)>[Latency];
    readonly List<(string Name, double Ms)> _cpu = [];
    readonly System.Diagnostics.Stopwatch _clock = new();
    double _lastCpuMark;
    readonly Stack<uint> _free = new();
    int _slot = -1;

    /// <summary>The timer the current frame's marks go to, if any.</summary>
    internal static GpuPassTimer? Current;

    /// <summary>The most recent completed frame: each pass and its milliseconds, in order.</summary>
    public IReadOnlyList<(string Pass, double Ms)> Last { get; private set; } = [];

    /// <summary>
    /// The CPU time spent issuing each pass of the last frame - mark to mark, like the GPU rows.
    /// A pass whose GPU time is no more than this was waiting on the CPU to send it work.
    /// </summary>
    public IReadOnlyList<(string Pass, double Ms)> LastCpu { get; private set; } = [];

    /// <summary>The most recent completed frame's total, first mark to last.</summary>
    public double LastTotalMs { get; private set; }

    public GpuPassTimer(GL gl)
    {
        _gl = gl;
        for (int i = 0; i < Latency; i++)
            _frames[i] = [];
    }

    /// <summary>Starts a frame: reads back the frame written <see cref="Latency"/> frames ago, if it is ready, and marks the start.</summary>
    public void BeginFrame()
    {
        _slot = (_slot + 1) % Latency;
        var old = _frames[_slot];
        if (old.Count > 1)
        {
            _gl.GetQueryObject(old[^1].Query, QueryObjectParameterName.ResultAvailable, out int ready);
            if (ready != 0)
            {
                var times = new ulong[old.Count];
                for (int i = 0; i < old.Count; i++)
                    _gl.GetQueryObject(old[i].Query, QueryObjectParameterName.Result, out times[i]);
                var passes = new List<(string, double)>(old.Count - 1);
                for (int i = 1; i < old.Count; i++)
                    passes.Add((old[i].Name, (times[i] - times[i - 1]) / 1e6));
                Last = passes;
                LastTotalMs = (times[^1] - times[0]) / 1e6;
            }
        }
        foreach (var (_, q) in old)
            _free.Push(q);
        old.Clear();
        if (_cpu.Count > 0)
            LastCpu = [.. _cpu];
        _cpu.Clear();
        _clock.Restart();
        _lastCpuMark = 0;
        Current = this;
        Mark("start");
    }

    /// <summary>Marks the end of the pass named <paramref name="pass"/>.</summary>
    public void Mark(string pass)
    {
        if (_slot < 0)
            return;
        uint q = _free.Count > 0 ? _free.Pop() : _gl.GenQuery();
        _gl.QueryCounter(q, QueryCounterTarget.Timestamp);
        _frames[_slot].Add((pass, q));
        double now = _clock.Elapsed.TotalMilliseconds;
        if (pass != "start")
            _cpu.Add((pass, now - _lastCpuMark));
        _lastCpuMark = now;
    }

    /// <summary>Ends the frame; later marks are ignored until the next <see cref="BeginFrame"/>.</summary>
    public void EndFrame(string lastPass)
    {
        Mark(lastPass);
        if (ReferenceEquals(Current, this))
            Current = null;
    }

    public void Dispose()
    {
        if (ReferenceEquals(Current, this))
            Current = null;
        foreach (var frame in _frames)
            foreach (var (_, q) in frame)
                _gl.DeleteQuery(q);
        while (_free.Count > 0)
            _gl.DeleteQuery(_free.Pop());
    }
}
