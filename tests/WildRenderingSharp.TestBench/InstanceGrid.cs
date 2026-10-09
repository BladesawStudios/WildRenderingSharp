using System.Numerics;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Rendering.Cameras;

namespace WildRenderingSharp.TestBench;

/// <summary>Places many copies of one model on a square grid as a single instanced batch, to exercise the path a map takes.</summary>
static class InstanceGrid
{
    // Returns the centre and radius of the grid, for framing it.
    public static (Vector3 Center, float Radius) Add(WildRenderer renderer, string modelName, int count, float spacingInRadii)
    {
        var model = renderer.LoadModel(modelName);
        float spacing = model.BoundsRadius * 2f * spacingInRadii;
        int side = (int)Math.Ceiling(Math.Sqrt(count));

        var placements = new List<Vector4[]>(count);
        for (int i = 0; i < count; i++)
        {
            float x = (i % side - (side - 1) / 2f) * spacing, z = (i / side - (side - 1) / 2f) * spacing;
            placements.Add([new Vector4(1, 0, 0, x), new Vector4(0, 1, 0, 0), new Vector4(0, 0, 1, z)]);
        }

        var batch = renderer.AddInstances(model, placements);
        batch.ShowAll();
        var center = (batch.BoundsMin + batch.BoundsMax) / 2f;
        float radius = (batch.BoundsMax - batch.BoundsMin).Length() / 2f;

        var framing = SceneFramingCalculator.ForModelRadius(radius);
        renderer.AoRadius = framing.AoRadius;
        renderer.ShadowBias = framing.ShadowBias;
        return (center, radius);
    }
}
