namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Measures what exposure the CURRENT scene actually needs, instead of leaving it a guessed
/// constant.
/// </summary>
/// <remarks>
/// <para>
/// WildRenderingSharp's <c>Exposure</c> defaults to 2.5 while the real game authors <c>Exposure: 1.0</c> in its
/// palettes. That factor is a stand-in for lighting WildRenderingSharp is missing. It was 9.5 for a
/// long time, calibrated while <c>cTex_DeferredLightPrePass</c> was identically zero (see
/// <see cref="LightPrePass"/>), so every <c>chara_*</c> resolve shader received literally no main
/// light. The default is now 2.5, chosen by hand - still a judgement, not a derivation.
/// </para>
/// <para>
/// This measures rather than guesses. It reads the HDR buffer BEFORE exposure is applied and takes
/// the GEOMETRIC mean of luminance, which is the standard choice for auto-exposure because it is
/// driven by the bulk of the image rather than by its brightest pixels - an arithmetic mean would
/// be dominated by a sun disc or a specular highlight and would swing wildly as one moves through
/// frame. Near-black pixels are excluded for the same reason in reverse: an empty viewport is
/// mostly background, and letting that set the level would push the exposure arbitrarily high.
/// </para>
/// <para>
/// It REPORTS, it does not apply. The right exposure is a judgement about how the scene should
/// look, and silently moving it would make every previous visual comparison incomparable.
/// </para>
/// </remarks>
public static class ExposureMeter
{
    /// <summary>Middle grey the geometric mean is aimed at - the usual photographic 18% reference.</summary>
    public const float TargetGrey = 0.18f;

    public readonly record struct Result(
        float GeometricMeanLuminance,
        float SuggestedExposure,
        float MaxLuminance,
        int SampleCount);

    /// <summary>
    /// Samples <paramref name="hdr"/> (pre-exposure) and returns the exposure that would put its
    /// geometric mean luminance at <see cref="TargetGrey"/>.
    /// </summary>
    /// <param name="stride">Sample every Nth pixel. The readback is the expensive part, and a mean over tens of thousands of samples is already far more stable than the eye can judge.</param>
    public static Result? Measure(RenderTargets targets, GpuTexture hdr, int stride = 4)
    {
        float[] px;
        try
        {
            px = targets.ReadPixelsFloatRgba(hdr);
        }
        catch
        {
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
