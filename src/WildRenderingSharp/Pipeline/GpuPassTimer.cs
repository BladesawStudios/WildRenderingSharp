using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// How long each pass of a frame took on the GPU, from timestamp queries written between passes and read back a few frames later,
/// so measuring never waits on the card.
/// </summary>
public sealed class GpuPassTimer : IDisposable
{
    const int Latency = 4;
    readonly GL _gl;
    readonly List<(string Name, uint Query)>[] _frames = new List<(string, uint)>[Latency];
    readonly List<(string Name, uint Query)>[] _details = new List<(string, uint)>[Latency];

    public bool Detailed { get; set; }

    public IReadOnlyList<(string Label, double Ms, int Count)> LastDetail { get; private set; } = [];
    readonly List<(string Name, double Ms)> _cpu = [];
    readonly System.Diagnostics.Stopwatch _clock = new();
    double _lastCpuMark;
    readonly Stack<uint> _free = new();
    int _slot = -1;

    internal static GpuPassTimer? Current;

    public IReadOnlyList<(string Pass, double Ms)> Last { get; private set; } = [];

    public IReadOnlyList<(string Pass, double Ms)> LastCpu { get; private set; } = [];

    public double LastTotalMs { get; private set; }

    public GpuPassTimer(GL gl)
    {
        _gl = gl;
        for (int i = 0; i < Latency; i++)
        {
            _frames[i] = [];
            _details[i] = [];
        }
    }

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

        var detail = _details[_slot];
        if (detail.Count > 1)
        {
            _gl.GetQueryObject(detail[^1].Query, QueryObjectParameterName.ResultAvailable, out int ready);
            if (ready != 0)
            {
                var sums = new Dictionary<string, (double Ms, int Count)>(StringComparer.Ordinal);
                _gl.GetQueryObject(detail[0].Query, QueryObjectParameterName.Result, out ulong previous);
                for (int i = 1; i < detail.Count; i++)
                {
                    _gl.GetQueryObject(detail[i].Query, QueryObjectParameterName.Result, out ulong t);
                    string label = detail[i].Name;
                    if (label.Length > 0)
                    {
                        sums.TryGetValue(label, out var s);
                        sums[label] = (s.Ms + (t - previous) / 1e6, s.Count + 1);
                    }
                    previous = t;
                }
                LastDetail = [.. sums.OrderByDescending(kv => kv.Value.Ms).Take(24).Select(kv => (kv.Key, kv.Value.Ms, kv.Value.Count))];
            }
        }
        foreach (var (_, q) in detail)
            _free.Push(q);
        detail.Clear();
        if (_cpu.Count > 0)
            LastCpu = [.. _cpu];
        _cpu.Clear();
        _clock.Restart();
        _lastCpuMark = 0;
        Current = this;
        Mark("start");
    }

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

    public void Detail(string label)
    {
        if (!Detailed || _slot < 0)
            return;
        uint q = _free.Count > 0 ? _free.Pop() : _gl.GenQuery();
        _gl.QueryCounter(q, QueryCounterTarget.Timestamp);
        _details[_slot].Add((label, q));
    }

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
        foreach (var frame in _frames.Concat(_details))
            foreach (var (_, q) in frame)
                _gl.DeleteQuery(q);
        while (_free.Count > 0)
            _gl.DeleteQuery(_free.Pop());
    }
}
