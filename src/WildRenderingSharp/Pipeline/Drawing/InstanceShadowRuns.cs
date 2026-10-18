using System.Numerics;
using WildRenderingSharp.Pipeline.Shadows;
using WildRenderingSharp.Pipeline.Targets;

namespace WildRenderingSharp.Pipeline.Drawing;

/// <summary>The runs of a batch's instances that can cast into the shadow map, found for the whole shadow region and for each cascade.</summary>
public sealed class InstanceShadowRuns(IReadOnlyList<Vector4[]> placements, float boundsRadius)
{
    // The level of detail shadow casters draw at: the finest. A caster also receives its own shadow, and a coarser level is a
    // different surface that stands proud in places and shadows it in hard-edged patches.
    const int ShadowLod = 0;

    // Placements are tested in groups first: a group lying wholly outside a region, or wholly inside it, is settled with one test of its bounding sphere.
    const int ChunkSize = 32;

    Vector3[]? _chunkCentre;
    float[]? _chunkRadius;

    readonly List<(int First, int Count, int Lod)>?[] _cascadeRuns = new List<(int, int, int)>?[RenderTargets.MaxCascades];
    readonly (ShadowFocus Focus, Vector3 Right, Vector3 Up)?[] _cascadeFocus = new (ShadowFocus, Vector3, Vector3)?[RenderTargets.MaxCascades];
    ShadowFocus? _focus;
    int _cascadeMask = ~0;

    public List<(int First, int Count, int Lod)> Visible { get; } = [];

    // Finding runs again is cheap for one batch and a long time for four thousand of them, which is what happens when the region they were found for moves: sixty
    // milliseconds in one frame. So a frame has a little time to spend on it, and a batch that finds the time gone keeps the runs it has, a frame or two stale, and
    // is found again in the next. A batch that has none yet is always found, so nothing is left out of a map altogether.
    static long _deadline;

    public static void BeginFrame(double milliseconds) =>
        _deadline = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(milliseconds * System.Diagnostics.Stopwatch.Frequency / 1000.0);

    static bool OutOfTime => System.Diagnostics.Stopwatch.GetTimestamp() > _deadline;

    bool _filledOnce;

    // A reach around a point beyond which a placement casts nothing: the distance the host draws the model to, so the maps hold what is drawn and not every placement of the
    // model within the light's square. A range of zero is no limit. The point is the host's to keep steady, since the runs are found again whenever it moves.
    Vector3 _limitEye;
    float _limitRange;

    public void Limit(Vector3 eye, float range)
    {
        if (eye == _limitEye && range == _limitRange)
            return;
        (_limitEye, _limitRange) = (eye, range);
        _focus = null;
        Array.Clear(_cascadeFocus);
    }

    bool WithinLimit(Vector3 position) =>
        _limitRange <= 0f || Vector3.DistanceSquared(position, _limitEye) <= _limitRange * _limitRange;

    // Which cascades the batch casts into; changing it makes every cascade recompute.
    public int CascadeMask
    {
        get => _cascadeMask;
        set
        {
            if (_cascadeMask == value)
                return;
            _cascadeMask = value;
            Array.Clear(_cascadeFocus);
        }
    }

    internal List<(int First, int Count, int Lod)> Cascade(int cascade) => _cascadeRuns[cascade] ??= [];

    internal void Update(ShadowFocus focus)
    {
        if (_focus == focus || (_filledOnce && OutOfTime))
            return;
        _focus = focus;
        _filledOnce = true;
        float reach = focus.Radius + boundsRadius;
        FillRuns(Visible, ShadowLod, position =>
        {
            Vector3 d = Vector3.Abs(position - focus.Center);
            return d.X <= reach && d.Y <= reach && d.Z <= reach;
        }, (centre, radius) =>
        {
            Vector3 d = Vector3.Abs(centre - focus.Center);
            if (MathF.Max(d.X, MathF.Max(d.Y, d.Z)) - radius > reach)
                return Chunk.Outside;
            return MathF.Max(d.X, MathF.Max(d.Y, d.Z)) + radius <= reach ? Chunk.Inside : Chunk.Partly;
        });
    }

    // Fills a cascade's runs when its region or the sun moved: every instance whose placement lies in the square the cascade's light projection
    // covers, so casters outside the region whose shadow falls inside are not left out. Far cascades draw coarser levels of detail.
    internal void UpdateCascade(int cascade, ShadowFocus focus, Vector3 right, Vector3 up, float halfExtent)
    {
        if (_cascadeFocus[cascade] == (focus, right, up) && _cascadeRuns[cascade] is not null)
            return;
        if (_cascadeRuns[cascade] is not null && OutOfTime)
            return;
        _cascadeFocus[cascade] = (focus, right, up);
        var runs = Cascade(cascade);
        runs.Clear();
        if ((_cascadeMask & (1 << cascade)) == 0)
            return;

        float reach = halfExtent + boundsRadius;
        // The near two cascades at full detail (see ShadowLod); past them the view draws coarse levels too.
        FillRuns(runs, Math.Max(ShadowLod, cascade - 1), position =>
        {
            var d = position - focus.Center;
            return MathF.Abs(Vector3.Dot(d, right)) <= reach && MathF.Abs(Vector3.Dot(d, up)) <= reach;
        }, (centre, radius) =>
        {
            var d = centre - focus.Center;
            float across = MathF.Max(MathF.Abs(Vector3.Dot(d, right)), MathF.Abs(Vector3.Dot(d, up)));
            if (across - radius > reach)
                return Chunk.Outside;
            return across + radius <= reach ? Chunk.Inside : Chunk.Partly;
        });
    }

    enum Chunk { Outside, Partly, Inside }

    // The sphere around each group of placements, found once: a batch's placements do not change.
    void EnsureChunks()
    {
        if (_chunkCentre is not null)
            return;
        int chunks = (placements.Count + ChunkSize - 1) / ChunkSize;
        _chunkCentre = new Vector3[chunks];
        _chunkRadius = new float[chunks];
        for (int c = 0; c < chunks; c++)
        {
            int first = c * ChunkSize, last = Math.Min(first + ChunkSize, placements.Count);
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            for (int i = first; i < last; i++)
            {
                var position = new Vector3(placements[i][0].W, placements[i][1].W, placements[i][2].W);
                lo = Vector3.Min(lo, position);
                hi = Vector3.Max(hi, position);
            }
            _chunkCentre[c] = (lo + hi) * 0.5f;
            _chunkRadius[c] = (hi - lo).Length() * 0.5f + 1e-3f;
        }
    }

    // Collects maximal stretches of consecutive instances whose position passes the test, settling whole groups by their sphere where it can.
    void FillRuns(List<(int First, int Count, int Lod)> runs, int lod, Func<Vector3, bool> inside, Func<Vector3, float, Chunk> chunkTest)
    {
        runs.Clear();
        EnsureChunks();
        int start = -1;
        for (int c = 0; c < _chunkCentre!.Length; c++)
        {
            int first = c * ChunkSize, last = Math.Min(first + ChunkSize, placements.Count);
            var settled = chunkTest(_chunkCentre[c], _chunkRadius![c]);
            if (_limitRange > 0f && settled != Chunk.Outside)
            {
                float away = Vector3.Distance(_chunkCentre[c], _limitEye);
                if (away - _chunkRadius![c] > _limitRange)
                    settled = Chunk.Outside;
                else if (away + _chunkRadius![c] > _limitRange)
                    settled = Chunk.Partly;
            }
            if (settled != Chunk.Partly)
            {
                if (settled == Chunk.Inside && start < 0)
                    start = first;
                else if (settled == Chunk.Outside && start >= 0)
                {
                    runs.Add((start, first - start, lod));
                    start = -1;
                }
                continue;
            }

            for (int i = first; i < last; i++)
            {
                var at = new Vector3(placements[i][0].W, placements[i][1].W, placements[i][2].W);
                bool passes = WithinLimit(at) && inside(at);
                if (passes && start < 0)
                {
                    start = i;
                }
                else if (!passes && start >= 0)
                {
                    runs.Add((start, i - start, lod));
                    start = -1;
                }
            }
        }
        if (start >= 0)
            runs.Add((start, placements.Count - start, lod));
    }
}
