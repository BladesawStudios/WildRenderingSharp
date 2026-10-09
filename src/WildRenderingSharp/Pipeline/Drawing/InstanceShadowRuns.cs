using System.Numerics;
using WildRenderingSharp.Pipeline.Gpu;
using WildRenderingSharp.Pipeline.Shadows;

namespace WildRenderingSharp.Pipeline.Drawing;

/// <summary>The runs of a batch's instances that can cast into the shadow map, found for the whole shadow region and for each cascade.</summary>
public sealed class InstanceShadowRuns(IReadOnlyList<Vector4[]> placements, float boundsRadius)
{
    // The level of detail shadow casters draw at: the finest. A caster also receives its own shadow, and a coarser level is a
    // different surface that stands proud in places and shadows it in hard-edged patches.
    const int ShadowLod = 0;

    readonly List<(int First, int Count, int Lod)>?[] _cascadeRuns = new List<(int, int, int)>?[RenderTargets.MaxCascades];
    readonly (ShadowFocus Focus, Vector3 Right, Vector3 Up)?[] _cascadeFocus = new (ShadowFocus, Vector3, Vector3)?[RenderTargets.MaxCascades];
    ShadowFocus? _focus;
    int _cascadeMask = ~0;

    public List<(int First, int Count, int Lod)> Visible { get; } = [];

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
        if (_focus == focus)
            return;
        _focus = focus;
        float reach = focus.Radius + boundsRadius;
        FillRuns(Visible, ShadowLod, position =>
        {
            Vector3 d = Vector3.Abs(position - focus.Center);
            return d.X <= reach && d.Y <= reach && d.Z <= reach;
        });
    }

    // Fills a cascade's runs if its region or the sun moved: every instance whose placement falls inside the square the cascade's light
    // projection covers, halfExtent along the light's right and up axes about the region's centre, at any depth along the sun. Choosing
    // by the region's own box left out casters standing outside it whose shadow falls inside. Far cascades draw coarser levels of detail.
    internal void UpdateCascade(int cascade, ShadowFocus focus, Vector3 right, Vector3 up, float halfExtent)
    {
        if (_cascadeFocus[cascade] == (focus, right, up) && _cascadeRuns[cascade] is not null)
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
        });
    }

    // Collects maximal stretches of consecutive instances whose position passes the test.
    void FillRuns(List<(int First, int Count, int Lod)> runs, int lod, Func<Vector3, bool> inside)
    {
        runs.Clear();
        int start = -1;
        for (int i = 0; i <= placements.Count; i++)
        {
            bool passes = i < placements.Count && inside(new Vector3(placements[i][0].W, placements[i][1].W, placements[i][2].W));
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
}
