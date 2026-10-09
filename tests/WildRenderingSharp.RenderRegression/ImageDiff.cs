namespace WildRenderingSharp.RenderRegression;

/// <summary>How far a render is from its baseline: the mean per-channel difference out of 255, and the share of pixels that differ visibly.</summary>
readonly record struct DiffStats(double MeanAbs, double FractionOver16)
{
    // A pixel counts as visibly different when any colour channel moves by more than this.
    public const int VisibleStep = 16;

    // Rendering is deterministic on one machine, so the default allows only rounding noise.
    public static DiffStats DefaultTolerance => new(0.05, 0.0001);

    public bool Within(DiffStats tolerance) => MeanAbs <= tolerance.MeanAbs && FractionOver16 <= tolerance.FractionOver16;
}

static class ImageDiff
{
    public static DiffStats Compare(Image baseline, Image current)
    {
        if (baseline.Width != current.Width || baseline.Height != current.Height)
            return new DiffStats(double.PositiveInfinity, 1);

        long sum = 0;
        int visible = 0, pixels = baseline.Width * baseline.Height;
        for (int i = 0; i < pixels; i++)
        {
            int largest = 0;
            for (int c = 0; c < 3; c++)
            {
                int d = Math.Abs(baseline.Rgba[i * 4 + c] - current.Rgba[i * 4 + c]);
                sum += d;
                largest = Math.Max(largest, d);
            }
            if (largest > DiffStats.VisibleStep)
                visible++;
        }
        return new DiffStats(sum / (pixels * 3.0), visible / (double)pixels);
    }

    // The difference at eight times its size, so a faint change is visible.
    public static Image Visualize(Image baseline, Image current)
    {
        var rgba = new byte[baseline.Rgba.Length];
        for (int i = 0; i < baseline.Rgba.Length; i++)
            rgba[i] = (i & 3) == 3 ? (byte)255 : (byte)Math.Min(255, Math.Abs(baseline.Rgba[i] - current.Rgba[i]) * 8);
        return baseline with { Rgba = rgba };
    }
}
