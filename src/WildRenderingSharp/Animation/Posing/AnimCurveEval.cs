namespace WildRenderingSharp.Animation.Posing;

/// <summary>
/// The one implementation of BFRES curve evaluation, shared by every kind of animation WildRenderingSharp plays - skeletal TRS
/// curves, texture pattern index curves and shader parameter curves all come out of the same <c>ResAnimCurve</c> and are evaluated
/// by the same two entry points in the game.
/// </summary>
internal static class AnimCurveEval
{
    public const int Cubic = 0x00;
    public const int Linear = 0x10;
    public const int BakedFloat = 0x20;
    public const int StepInt = 0x40;
    public const int BakedInt = 0x50;
    public const int StepBool = 0x60;
    public const int BakedBool = 0x70;

    public static bool IsIntCurve(int curveType) => curveType >= StepInt;

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

    public static int FindFrame(float[] frames, float f)
    {
        int i = 0;
        for (; i < frames.Length - 2 && frames[i + 1] <= f; i++) { }
        return i;
    }
}
