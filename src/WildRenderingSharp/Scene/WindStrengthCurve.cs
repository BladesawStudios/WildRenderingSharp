using System.Numerics;

namespace WildRenderingSharp.Scene;

/// <summary>A reusable looping cubic Bezier-style scalar envelope.</summary>
public sealed class WindStrengthCurve
{
    public List<Vector2> Points { get; } =
    [
        new(0f, 0.25f),
        new(0.28f, 1.15f),
        new(0.58f, 0.45f),
        new(0.82f, 0.95f),
        new(1f, 0.25f),
    ];

    public float Evaluate(float phase)
    {
        if (Points.Count == 0) return 1f;
        phase = phase - MathF.Floor(phase);
        int right = Points.FindIndex(point => point.X >= phase);
        if (right <= 0) return Points[0].Y;
        if (right < 0) return Points[^1].Y;

        Vector2 a = Points[right - 1];
        Vector2 b = Points[right];
        float span = MathF.Max(1e-6f, b.X - a.X);
        float u = Math.Clamp((phase - a.X) / span, 0f, 1f);

        // Cubic Bezier with horizontal handles at one third of the segment. Since the handles'
        // Y values equal their anchors, this is the familiar smoothstep-shaped Bezier transition:
        // editable anchors, no surprise overshoot, and zero slope where gust keys meet.
        float smooth = u * u * (3f - 2f * u);
        return MathF.Max(0f, a.Y + (b.Y - a.Y) * smooth);
    }

    public int AddPoint(Vector2 point)
    {
        point = new Vector2(Math.Clamp(point.X, 0.01f, 0.99f), Math.Clamp(point.Y, 0f, 2f));
        int index = Points.FindIndex(existing => existing.X > point.X);
        if (index < 0) index = Points.Count - 1;
        Points.Insert(Math.Max(1, index), point);
        return Math.Max(1, index);
    }

    public void SetPoint(int index, Vector2 point)
    {
        if ((uint)index >= (uint)Points.Count) return;
        float minX = index == 0 ? 0f : Points[index - 1].X + 0.005f;
        float maxX = index == Points.Count - 1 ? 1f : Points[index + 1].X - 0.005f;
        float x = index == 0 ? 0f : index == Points.Count - 1 ? 1f : Math.Clamp(point.X, minX, maxX);
        float y = Math.Clamp(point.Y, 0f, 2f);
        Points[index] = new Vector2(x, y);

        // The seam is one logical key. Keeping its values equal prevents an impulse when the
        // envelope wraps from phase 1 back to phase 0.
        if (index == 0) Points[^1] = new Vector2(1f, y);
        else if (index == Points.Count - 1) Points[0] = new Vector2(0f, y);
    }

    public void Reset()
    {
        Points.Clear();
        Points.AddRange([new(0f, 0.25f), new(0.28f, 1.15f), new(0.58f, 0.45f), new(0.82f, 0.95f), new(1f, 0.25f)]);
    }
}
