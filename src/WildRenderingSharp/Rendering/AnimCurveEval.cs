namespace WildRenderingSharp.Rendering;

/// <summary>
/// The one implementation of BFRES curve evaluation, shared by every kind of animation WildRenderingSharp
/// plays - skeletal TRS curves, texture pattern index curves and shader parameter curves all come
/// out of the same <c>ResAnimCurve</c> and are evaluated by the same two entry points in the game.
///
/// Reverse engineered from the shipped code, not guessed:
///   <c>ResAnimCurve::EvaluateFloat</c> (Ghidra 0x7100073d90) finishes with
///     <c>Offset + raw * Scale</c>, reading <c>ResAnimCurve[0x24]</c> as a FLOAT;
///   <c>ResAnimCurve::EvaluateInt</c> (0x71009b774c) finishes with <c>Offset + raw</c> - the same
///     field read as an INT, and with NO Scale multiply at all;
///   <c>EvaluateCubic&lt;float&gt;</c> (0x71000733b4) treats the 4 keys per segment as already-baked
///     polynomial COEFFICIENTS, not Hermite value/tangent pairs;
///   <c>EvaluateLinear&lt;float&gt;</c> (0x71000736d0) uses the same normalized t, so key[1] is the
///     whole segment's delta rather than a per-frame slope;
///   <c>EvaluateBakedFloat&lt;float&gt;</c> (0x7100073984) has no frame list - one value per integer
///     frame, blended by the fractional part;
///   the step evaluator behind <c>EvaluateInt</c> (0x7100073a68) returns <c>keys[FindFrame(frame)]</c>
///     with no interpolation of any kind;
///   <c>FindFrame&lt;float&gt;</c> (0x710007316c) is the segment search all of them share.
///
/// Frames outside <c>[startFrame, endFrame]</c> are CLAMPED here. The game additionally supports
/// repeat/mirror/relative-repeat wrapping (the pre/post wrap bits of <c>ResAnimCurve[0x10]</c>),
/// which only differ outside an anim's own range - and WildRenderingSharp's playback already keeps the frame
/// inside it.
/// </summary>
public static class AnimCurveEval
{
    /// <summary>Curve type values, straight from <c>BfresLibrary.AnimCurveType</c> and matching the game's own dispatch on <c>(flags &amp; 0x70)</c>.</summary>
    public const int Cubic = 0x00;
    public const int Linear = 0x10;
    public const int BakedFloat = 0x20;
    public const int StepInt = 0x40;
    public const int BakedInt = 0x50;
    public const int StepBool = 0x60;
    public const int BakedBool = 0x70;

    /// <summary>True for the curve types whose evaluated value is an integer bit pattern rather than a float.</summary>
    public static bool IsIntCurve(int curveType) => curveType >= StepInt;

    /// <summary><c>Offset + raw * Scale</c> - see the class remarks.</summary>
    public static float EvaluateFloat(int curveType, float startFrame, float endFrame, float scale, float offset,
        float[] frames, float[][] keys, float frame)
    {
        if (keys.Length == 0)
            return offset;
        float f = Math.Clamp(frame, startFrame, endFrame);
        float raw = curveType switch
        {
            BakedFloat => Baked(startFrame, keys, f),
            Linear => Segment(frames, keys, f, cubic: false),
            _ => Segment(frames, keys, f, cubic: true),
        };
        return offset + raw * scale;
    }

    /// <summary><c>Offset + raw</c>, step-held and with NO Scale multiply - <c>EvaluateInt</c> does not apply one.</summary>
    public static int EvaluateInt(float startFrame, float endFrame, int offset, float[] frames, float[][] keys, float frame)
    {
        if (keys.Length == 0)
            return offset;
        float f = Math.Clamp(frame, startFrame, endFrame);
        int i = 0;
        if (frames.Length > 0)
        {
            while (i + 1 < frames.Length && frames[i + 1] <= f)
                i++;
        }
        else
        {
            // A baked int curve has no frame list: one key per integer frame from startFrame.
            i = Math.Clamp((int)f - (int)startFrame, 0, keys.Length - 1);
        }
        return offset + (int)MathF.Round(keys[Math.Min(i, keys.Length - 1)][0]);
    }

    static float Baked(float startFrame, float[][] keys, float f)
    {
        int idx = (int)f - (int)startFrame;
        int last = keys.Length - 1;
        if (idx >= last)
            return keys[last][0];
        if (idx < 0)
            return keys[0][0];
        float frac = f - MathF.Floor(f);
        return keys[idx][0] * (1f - frac) + keys[idx + 1][0] * frac;
    }

    static float Segment(float[] frames, float[][] keys, float f, bool cubic)
    {
        if (frames.Length < 2)
            return keys[0][0];
        int i = FindFrame(frames, f);
        float span = frames[i + 1] - frames[i];
        float t = span != 0f ? (f - frames[i]) / span : 0f;
        var key = keys[Math.Min(i, keys.Length - 1)];
        if (cubic)
            return key.Length >= 4 ? key[0] + t * (key[1] + t * (key[2] + t * key[3])) : key[0];
        return key.Length >= 2 ? key[0] + t * key[1] : key[0];
    }

    /// <summary>Largest i such that <c>frames[i] &lt;= f</c>, clamped so <c>i+1</c> stays in range - matches <c>ResAnimCurve::FindFrame</c>'s linear-search fallback (per-curve keyframe counts are small enough that its cached-hint fast path isn't worth reproducing).</summary>
    public static int FindFrame(float[] frames, float f)
    {
        int i = 0;
        for (; i < frames.Length - 2 && frames[i + 1] <= f; i++) { }
        return i;
    }
}
