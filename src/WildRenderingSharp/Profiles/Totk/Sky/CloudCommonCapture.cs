namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>
/// The Common block read from the game's own cloud draw in a capture, in its first weather, and a check of a built block against it. The slots
/// the weather and the clock animate and the per-palette or per-camera ones are not compared.
/// </summary>
static class CloudCommonCapture
{
    static readonly (int Slot, float X, float Y, float Z, float W)[] Slots =
    [
        (1, float.NaN, float.NaN, float.NaN, 4f),
        (2, 8f, 0.5f, 0.5f, 0.05f),
        (3, -0.0066f, 0.01f, 0.1f, 1.3f),
        (4, 0.05f, float.NaN, float.NaN, 1.2f),
        (5, 0.5f, 0.35f, 0.7f, float.NaN),
        (7, 8f, 1.8f, 0.8f, 0.15f),
        (8, 0.15f, 0.7f, 0.95f, -1f),
        // [25].y carries the brightness gain, so it is expected to differ from the captured 2.75.
        (25, 0.393401f, float.NaN, 0.363636f, 6f),
        (26, 0.9f, 0.015f, 0.75f, 0.85f),
        (27, 8f, 0f, 0f, 0f),
        (37, 0.00333333f, 0f, 0f, 83.3301f),
        (38, 0f, 0.5f, 0f, 0f),
        (40, 3.33333e-05f, 0f, 0f, 0f),
        (43, 1f, 0.25f, 6f, 2f),
        (44, 1f, 0.25f, 0f, 0f),
        (45, 0f, 1f, 0f, 0f),
        (46, 25000f, 100f, 0f, 0f),
    ];

    public static void Report(ReadOnlySpan<byte> common)
    {
        var differences = new List<string>();
        foreach (var (slot, x, y, z, w) in Slots)
        {
            float[] captured = [x, y, z, w];
            for (int component = 0; component < 4; component++)
            {
                if (float.IsNaN(captured[component]))
                    continue;
                float built = BitConverter.ToSingle(common[(slot * 16 + component * 4)..]);
                float tolerance = MathF.Max(1e-4f, MathF.Abs(captured[component]) * 1e-3f);
                if (MathF.Abs(built - captured[component]) > tolerance)
                    differences.Add($"[{slot}].{"xyzw"[component]} ours={built:G6} game={captured[component]:G6}");
            }
        }
        Console.WriteLine(differences.Count == 0
            ? "[CloudDomePass] Common block matches the captured game block on every compared slot."
            : $"[CloudDomePass] Common block DIFFERS from the game on {differences.Count} component(s): {string.Join("  ", differences)}");
    }
}
