using System.Globalization;
using BfShaderSharp.Pack.Records;

namespace KingRenderingSharp.Content.Packs;

/// <summary>A pack curve as the animation loader evaluates it: the curve type, frames, one row of keys for each frame, and the scale and offset applied to the result.</summary>
/// <remarks>
/// The curve type is the flags' (0 cubic, 0x10 linear, 0x20 baked float, 0x40 stepped integer, 0x50 baked integer, 0x60 stepped boolean, 0x70 baked boolean). A cubic row is four
/// coefficients, a linear row two values, any other row one value. A compressed cubic curve is decoded to its values and slopes and written as cubic coefficients.
/// </remarks>
sealed record PackCurve(int Type, float[] Frames, IReadOnlyList<float[]> Rows, float Scale, float Offset)
{
    const int CurveTypeMask = 0x70;

    /// <summary>Reads the curve, or returns null with the reason it cannot be carried over.</summary>
    public static PackCurve? Read(CurveRecord curve, out string? reason)
    {
        reason = null;
        int type = curve.Flags & CurveTypeMask;
        float[] frames = curve.Frames;
        List<float[]> rows = [];
        if (CurveDecoder.CanDecode(curve))
        {
            HermiteKeys keys = CurveDecoder.Decode(curve);
            frames = keys.Frames;
            for (int i = 0; i < frames.Length; i++)
            {
                if (i == frames.Length - 1) { rows.Add([keys.Values[i], 0, 0, 0]); break; }
                float dt = frames[i + 1] - frames[i], v0 = keys.Values[i], v1 = keys.Values[i + 1], s0 = keys.Slopes[i], s1 = keys.Slopes[i + 1];
                float d = v0 - v1;
                rows.Add(dt == 0 ? [v0, s0, 0, 0] : [v0, s0, -3 * d / (dt * dt) - (2 * s0 + s1) / dt, 2 * d / (dt * dt * dt) + (s0 + s1) / (dt * dt)]);
            }

            return new PackCurve(0, frames, rows, 1, 0);
        }

        if (curve.CompressedKeys.Length > 0)
        {
            reason = $"a compressed curve of flags 0x{curve.Flags:x} is not decoded";
            return null;
        }

        switch (type)
        {
            case 0x40 or 0x50:
                // A narrow integer curve holds its keys as floats beside the empty integer list.
                rows.AddRange(curve.IntKeys.Length > 0 ? curve.IntKeys.Select(k => new float[] { k }) : curve.Keys.Select(k => new float[] { k }));
                break;
            case 0x60 or 0x70:
                rows.AddRange(curve.BoolKeys.Length > 0 ? curve.BoolKeys.Select(k => new float[] { k }) : curve.Keys.Select(k => new float[] { k }));
                break;
            case 0 or 0x10 or 0x20:
                int per = type == 0x20 || frames.Length == 0 ? 1 : curve.Keys.Length / Math.Max(1, frames.Length);
                if (curve.Keys.Length == 0 || per == 0 || curve.Keys.Length % per != 0)
                {
                    reason = $"a curve has {curve.Keys.Length} keys for {frames.Length} frames";
                    return null;
                }

                for (int i = 0; i < curve.Keys.Length / per; i++) rows.Add(curve.Keys[(i * per)..(i * per + per)]);
                break;
            default:
                reason = $"a curve of flags 0x{curve.Flags:x} is not written";
                return null;
        }

        if (rows.Count == 0)
        {
            reason = $"a curve of flags 0x{curve.Flags:x} has no keys (frames {frames.Length}, floats {curve.Keys.Length}, ints {curve.IntKeys.Length}, bools {curve.BoolKeys.Length})";
            return null;
        }

        return new PackCurve(type, frames, rows, curve.Scale, curve.Offset);
    }

    public static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static string Floats(IEnumerable<float> values) => string.Join(", ", values.Select(F));

    public string KeysJson() => string.Join(", ", Rows.Select(r => $"[{Floats(r)}]"));
}
