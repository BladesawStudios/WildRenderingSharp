using System.Numerics;
using WildRenderingSharp.Gpu;

namespace WildRenderingSharp.Pipeline.Drawing;

/// <summary>The instance buffer's contents: per instance, three placement rows, the baked-lighting row, and the bone palette already placed in the world.</summary>
sealed record InstanceData(Vector4[] Vectors, Vector3 BoundsMin, Vector3 BoundsMax)
{
    public const int Row8Offset = 3, PaletteOffset = 4;

    // A model with no skeleton repeats its placement as a one-slot palette.
    public static InstanceData Pack(IReadOnlyList<Vector4[]> placements, Vector3 modelMin, Vector3 modelMax, Matrix4x4[] bindPalette, int stride)
    {
        bool repeats = bindPalette.Length == 0;
        var vectors = new Vector4[Math.Max(1, placements.Count) * stride];
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        for (int i = 0; i < placements.Count; i++)
        {
            Vector4[] rows = placements[i];
            int at = i * stride;
            rows.AsSpan(0, 3).CopyTo(vectors.AsSpan(at, 3));
            if (repeats)
                rows.AsSpan(0, 3).CopyTo(vectors.AsSpan(at + PaletteOffset, 3));
            else
                WritePalette(vectors, at + PaletteOffset, bindPalette, GpuMatrix.FromRows(rows));
            GrowBounds(modelMin, modelMax, rows, ref lo, ref hi);
        }
        return new InstanceData(vectors, placements.Count > 0 ? lo : Vector3.Zero, placements.Count > 0 ? hi : Vector3.Zero);
    }

    static void WritePalette(Vector4[] vectors, int at, Matrix4x4[] bindPalette, Matrix4x4 placement)
    {
        for (int s = 0; s < bindPalette.Length; s++)
        {
            Matrix4x4 m = bindPalette[s] * placement;
            int p = at + s * 3;
            vectors[p] = new Vector4(m.M11, m.M21, m.M31, m.M41);
            vectors[p + 1] = new Vector4(m.M12, m.M22, m.M32, m.M42);
            vectors[p + 2] = new Vector4(m.M13, m.M23, m.M33, m.M43);
        }
    }

    // Grows the world bounds by the eight corners of the model's box under the placement.
    static void GrowBounds(Vector3 modelMin, Vector3 modelMax, Vector4[] rows, ref Vector3 lo, ref Vector3 hi)
    {
        for (int c = 0; c < 8; c++)
        {
            var corner = new Vector4(
                (c & 1) == 0 ? modelMin.X : modelMax.X,
                (c & 2) == 0 ? modelMin.Y : modelMax.Y,
                (c & 4) == 0 ? modelMin.Z : modelMax.Z,
                1);
            var world = new Vector3(Vector4.Dot(rows[0], corner), Vector4.Dot(rows[1], corner), Vector4.Dot(rows[2], corner));
            lo = Vector3.Min(lo, world);
            hi = Vector3.Max(hi, world);
        }
    }
}
