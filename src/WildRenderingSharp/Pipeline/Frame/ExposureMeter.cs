using WildRenderingSharp.Gpu;
using WildRenderingSharp.Logging;
using WildRenderingSharp.Pipeline.Targets;

namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>Measures what exposure the CURRENT scene actually needs, instead of leaving it a guessed constant.</summary>
public static class ExposureMeter
{
    public const float TargetGrey = 0.18f;

    public readonly record struct Result(
        float GeometricMeanLuminance,
        float SuggestedExposure,
        float MaxLuminance,
        int SampleCount);

    public static Result? Measure(RenderTargets targets, GpuTexture hdr, int stride = 4)
    {
        float[] px;
        try
        {
            px = targets.ReadPixelsFloatRgba(hdr);
        }
        catch (Exception ex)
        {
            Log.Warning($"[ExposureMeter] could not read the frame back: {ex.Message}");
            return null;
        }
        if (px.Length < 4)
            return null;

        // Rec.709, matching the weighting the rest of the pipeline uses.
        double logSum = 0;
        int count = 0;
        float max = 0f;
        for (int i = 0; i + 2 < px.Length; i += 4 * stride)
        {
            float r = px[i], g = px[i + 1], b = px[i + 2];
            if (!float.IsFinite(r) || !float.IsFinite(g) || !float.IsFinite(b))
                continue;
            float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            if (lum > max) max = lum;
            // Excluded, not floored: a floor would drag the mean toward the floor value and make an
            // empty viewport report a confidently wrong number.
            if (lum <= 1e-5f)
                continue;
            logSum += Math.Log(lum);
            count++;
        }

        if (count == 0)
            return null;

        float mean = (float)Math.Exp(logSum / count);
        float suggested = mean > 1e-6f ? TargetGrey / mean : 1f;
        return new Result(mean, suggested, max, count);
    }
}
