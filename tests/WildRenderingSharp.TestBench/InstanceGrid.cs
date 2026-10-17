using System.Numerics;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Rendering.Cameras;

namespace WildRenderingSharp.TestBench;

/// <summary>Places many copies of one model on a square grid as a single instanced batch, to exercise the path a map takes.</summary>
static class InstanceGrid
{
    // Several models, each a few copies in a cell of its own, the cells in rows, so a scene has many kinds of shape in view at once; a model too large for its cell is
    // left out. Returns the centre and radius of all of them.
    public static (Vector3 Center, float Radius) AddRow(WildRenderer renderer, IReadOnlyList<string> modelNames, int count, float cell)
    {
        int columns = (int)Math.Ceiling(Math.Sqrt(modelNames.Count));
        int side = (int)Math.Ceiling(Math.Sqrt(count));
        float step = cell / side;
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        int placed = 0;
        foreach (string name in modelNames)
        {
            var model = renderer.LoadModel(name);
            if (model.BoundsRadius * 2f > step)
                continue;
            var placements = new List<Vector4[]>(count);
            for (int i = 0; i < count; i++)
            {
                float x = (placed % columns) * cell + (i % side - (side - 1) / 2f) * step, z = (placed / columns) * cell + (i / side - (side - 1) / 2f) * step;
                placements.Add([new Vector4(1, 0, 0, x), new Vector4(0, 1, 0, 0), new Vector4(0, 0, 1, z)]);
            }
            var batch = renderer.AddInstances(model, placements);
            batch.ShowAll();
            lo = Vector3.Min(lo, batch.BoundsMin);
            hi = Vector3.Max(hi, batch.BoundsMax);
            placed++;
        }
        Console.WriteLine($"mixed scene: {placed} of {modelNames.Count} models placed");
        var framing = SceneFramingCalculator.ForModelRadius((hi - lo).Length() / 2f);
        renderer.AoRadius = framing.AoRadius;
        renderer.ShadowBias = framing.ShadowBias;
        return ((lo + hi) / 2f, (hi - lo).Length() / 2f);
    }

    // Returns the centre and radius of the grid, for framing it.
    public static (Vector3 Center, float Radius) Add(WildRenderer renderer, string modelName, int count, float spacingInRadii, Vector3 origin = default, ulong bakeHash = 0)
    {
        var model = renderer.LoadModel(modelName);
        float spacing = model.BoundsRadius * 2f * spacingInRadii;
        int side = (int)Math.Ceiling(Math.Sqrt(count));

        var placements = new List<Vector4[]>(count);
        for (int i = 0; i < count; i++)
        {
            float x = (i % side - (side - 1) / 2f) * spacing, z = (i / side - (side - 1) / 2f) * spacing;
            placements.Add([new Vector4(1, 0, 0, x + origin.X), new Vector4(0, 1, 0, origin.Y), new Vector4(0, 0, 1, z + origin.Z)]);
        }

        var batch = renderer.AddInstances(model, placements);
        batch.ShowAll();
        // The baked lighting a placement of this hash has in the map, applied to every copy.
        if (bakeHash != 0 && renderer.FindBakes([bakeHash]).PerInstance[0] is { } bake)
            renderer.ApplyBake(batch, [.. Enumerable.Repeat<WildRenderingSharp.Assets.Baking.BakeActor?>(bake, count)]);
        var center = (batch.BoundsMin + batch.BoundsMax) / 2f;
        float radius = (batch.BoundsMax - batch.BoundsMin).Length() / 2f;

        var framing = SceneFramingCalculator.ForModelRadius(radius);
        renderer.AoRadius = framing.AoRadius;
        renderer.ShadowBias = framing.ShadowBias;
        return (center, radius);
    }
}
