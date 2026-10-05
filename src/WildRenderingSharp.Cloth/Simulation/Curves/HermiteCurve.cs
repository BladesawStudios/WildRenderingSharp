using System;
using System.Collections.Generic;
using WildRenderingSharp.Cloth.Model.HelperBone;

namespace WildRenderingSharp.Cloth.Simulation.Curves;

/// <summary>
/// Evaluates cubic Hermite splines with C1 continuity across keyframes.
/// Conforms to Nintendo Phive / Bfres connection curve specifications.
/// </summary>
public static class HermiteCurve
{
    public static float Evaluate(IReadOnlyList<HermiteKey> keys, float t)
    {
        if (keys == null || keys.Count == 0)
            return 0f;

        if (keys.Count == 1)
            return keys[0].Value;

        // Clamp at lower and upper bounds (standard Phive behaviour)
        if (t <= keys[0].Time)
            return keys[0].Value;

        int lastIdx = keys.Count - 1;
        if (t >= keys[lastIdx].Time)
            return keys[lastIdx].Value;

        // Binary search for interval [i, i + 1]
        int low = 0;
        int high = lastIdx;

        while (low <= high)
        {
            int mid = (low + high) >> 1;
            if (keys[mid].Time <= t)
            {
                if (mid + 1 < keys.Count && keys[mid + 1].Time > t)
                {
                    low = mid;
                    break;
                }
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        int i = Math.Clamp(low, 0, lastIdx - 1);
        var k0 = keys[i];
        var k1 = keys[i + 1];

        float h = k1.Time - k0.Time;
        if (h < 1e-7f)
            return k0.Value;

        float s = (t - k0.Time) / h;
        float s2 = s * s;
        float s3 = s2 * s;

        float h00 = 2f * s3 - 3f * s2 + 1f;
        float h10 = s3 - 2f * s2 + s;
        float h01 = -2f * s3 + 3f * s2;
        float h11 = s3 - s2;

        return h00 * k0.Value + h10 * (h * k0.OutSlope) + h01 * k1.Value + h11 * (h * k1.InSlope);
    }
}
