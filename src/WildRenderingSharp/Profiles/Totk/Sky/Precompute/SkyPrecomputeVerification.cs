using Silk.NET.OpenGL;
using WildRenderingSharp.Logging;
using static WildRenderingSharp.Profiles.Totk.Sky.Precompute.SkyPrecomputePass;

namespace WildRenderingSharp.Profiles.Totk.Sky.Precompute;

/// <summary>Checks a finished sky precompute run: the SizeInfo block against the game's, and each solved table for non-finite or negative values.</summary>
static class SkyPrecomputeVerification
{
    static readonly float[] CapturedSizeInfo =
    [
        256f, 64f, 0.00390625f, 0.015625f,
        64f, 64f, 0.015625f, 0.015625f,
        256f, 256f, 0.00390625f, 0.00390625f,
        64f, 64f, 0.015625f, 0.015625f,
        32f, 0.03125f, 0.032258064f, 0.06666667f,
        32f, 0.03125f, 0.032258064f, 0.06666667f,
        8f, 0.125f, 0.14285715f, 0.33333334f,
        16f, 0.0625f, 0.06666667f, 0.14285715f,
        6360f, 6420f, 0f, 0f,
    ];

    public static (bool Ok, float WorstError, int WorstSlot) VerifySizeInfoAgainstCapture()
    {
        ReadOnlySpan<byte> built = SkyPrecomputeBlocks.BuildSizeInfo().Bytes.Span;
        float worst = 0f;
        int worstIndex = -1;
        for (int i = 0; i < CapturedSizeInfo.Length; i++)
        {
            float got = BitConverter.ToSingle(built[(i * 4)..]);
            float want = CapturedSizeInfo[i];
            float error = want == 0f ? MathF.Abs(got) : MathF.Abs(got - want) / MathF.Max(1e-6f, MathF.Abs(want));
            if (error > worst)
            {
                worst = error;
                worstIndex = i;
            }
        }
        return (worst < 1e-6f, worst, worstIndex / 4);
    }

    public static bool Run(GL gl, SkyTables tables, int scatteringOrders)
    {
        var (sizeOk, sizeError, sizeSlot) = VerifySizeInfoAgainstCapture();
        Log.Info("[SkyPrecomputePass] SizeInfo vs real capture: " +
            (sizeOk ? "EXACT" : $"MISMATCH (worst rel err {sizeError:E2} at slot {sizeSlot})"));

        bool ok = sizeOk;
        ok &= Report("transmittance", ReadBack2D(gl, tables.Transmittance, TransmittanceW, TransmittanceH));
        ok &= Report("irradiance", ReadBack2D(gl, tables.Irradiance, IrradianceW, IrradianceH));
        ok &= Report("deltaSR", ReadBack3D(gl, tables.DeltaSR));
        ok &= Report("deltaSM", ReadBack3D(gl, tables.DeltaSM));
        ok &= Report("inscatter", ReadBack3D(gl, tables.Inscatter));

        float[] baked = ReadBack2D(gl, tables.BakedInscatter, BakedInscatterW, BakedInscatterH);
        ok &= Report("bakedInscat", baked);
        ReportBakedChannels(baked);
        DumpInscatter(gl, tables);

        Log.Info($"[SkyPrecomputePass] chain ({scatteringOrders} scattering orders) -> {(ok ? "PASS" : "FAIL")}");
        return ok;
    }

    static bool Report(string name, float[] texels)
    {
        var (min, max, nonFinite, negative) = Stats(texels);
        bool good = nonFinite == 0 && negative == 0 && max > 0f;
        Log.Info($"[SkyPrecomputePass]   {name,-12} range [{min:E3}, {max:E3}] non-finite={nonFinite} negative={negative} -> {(good ? "ok" : "BAD")}");
        return good;
    }

    // RGB and alpha separately: all-zero RGB with a healthy alpha reads as a normal range while rendering black.
    static void ReportBakedChannels(float[] baked)
    {
        var rgb = new float[baked.Length / 4 * 3];
        var alpha = new float[baked.Length / 4];
        for (int i = 0, j = 0; i < baked.Length; i += 4, j++)
        {
            rgb[j * 3] = baked[i];
            rgb[j * 3 + 1] = baked[i + 1];
            rgb[j * 3 + 2] = baked[i + 2];
            alpha[j] = baked[i + 3];
        }
        var (rgbMin, rgbMax, _, _) = Stats(rgb);
        var (alphaMin, alphaMax, _, _) = Stats(alpha);
        Log.Info($"[SkyPrecomputePass]   bakedInscat RGB range [{rgbMin:E3}, {rgbMax:E3}], ALPHA range [{alphaMin:E3}, {alphaMax:E3}]");
    }

    // Written when WRS_SKY_DUMP names a file, for diffing against a table lifted from a GPU capture (8 MB).
    static void DumpInscatter(GL gl, SkyTables tables)
    {
        string? path = Environment.GetEnvironmentVariable("WRS_SKY_DUMP");
        if (string.IsNullOrEmpty(path))
            return;
        float[] texels = ReadBack3D(gl, tables.Inscatter);
        var bytes = new byte[texels.Length * 4];
        System.Buffer.BlockCopy(texels, 0, bytes, 0, bytes.Length);
        File.WriteAllBytes(path, bytes);
        Log.Info($"[SkyPrecomputePass] dumped inscatter ({InscatterW}x{InscatterH}x{InscatterD}, float32 RGBA) to {path}");
    }

    static unsafe float[] ReadBack2D(GL gl, uint texture, int width, int height)
    {
        var texels = new float[width * height * 4];
        gl.BindTexture(TextureTarget.Texture2D, texture);
        fixed (float* p = texels)
            gl.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.Float, p);
        return texels;
    }

    static unsafe float[] ReadBack3D(GL gl, uint texture)
    {
        var texels = new float[InscatterW * InscatterH * InscatterD * 4];
        gl.BindTexture(TextureTarget.Texture3D, texture);
        fixed (float* p = texels)
            gl.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.Rgba, PixelType.Float, p);
        return texels;
    }

    static (float Min, float Max, int NonFinite, int Negative) Stats(float[] values)
    {
        float min = float.MaxValue, max = float.MinValue;
        int nonFinite = 0, negative = 0;
        foreach (float v in values)
        {
            if (!float.IsFinite(v))
            {
                nonFinite++;
                continue;
            }
            if (v < -1e-4f)
                negative++;
            min = MathF.Min(min, v);
            max = MathF.Max(max, v);
        }
        return nonFinite == values.Length ? (float.NaN, float.NaN, nonFinite, negative) : (min, max, nonFinite, negative);
    }
}
