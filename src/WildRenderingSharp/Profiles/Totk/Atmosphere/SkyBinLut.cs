using System.Numerics;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>
/// The real precomputed sky-scattering lookup table from <c>res/master_field.skybin</c> (<c>Env/GameScene.Nin_NX_NVN.genvb.zs</c>)
/// - see <c>ShaderLibrary.CompileTool.SkyBinTexture</c>'s own remarks for the reverse-engineered format and the
/// confirmed/unconfirmed parts of it.
/// </summary>
public sealed class SkyBinLut
{
    public const int Depth = 37;
    public const int CellCount = 8;

    // [z, cellY, cellX] -> RGBA
    readonly Vector4[,,] _cells;

    SkyBinLut(Vector4[,,] cells) => _cells = cells;

    public bool IsReal { get; private init; }

    public static readonly SkyBinLut Empty = new(new Vector4[Depth, CellCount, CellCount]) { IsReal = false };

    public static SkyBinLut LoadFromCache(string skyDataDirectory)
    {
        string path = Path.Combine(skyDataDirectory, "sky_lut.bin");
        if (!File.Exists(path))
        {
            Console.WriteLine($"[SkyBinLut] '{path}' not found - TotK Sky background falls back to the procedural approximation only.");
            return Empty;
        }

        try
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            char[] magic = r.ReadChars(4);
            if (new string(magic) != "SKYL")
                throw new InvalidDataException("bad magic");
            int depth = r.ReadInt32();
            int cellCount = r.ReadInt32();
            if (depth != Depth || cellCount != CellCount)
                throw new InvalidDataException($"unexpected dims {depth}x{cellCount}x{cellCount}, expected {Depth}x{CellCount}x{CellCount}");

            var cells = new Vector4[Depth, CellCount, CellCount];
            for (int z = 0; z < Depth; z++)
                for (int y = 0; y < CellCount; y++)
                    for (int x = 0; x < CellCount; x++)
                        cells[z, y, x] = new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

            Console.WriteLine("[SkyBinLut] loaded real sky-scattering LUT from cache.");
            return new SkyBinLut(cells) { IsReal = true };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SkyBinLut] failed to read '{path}': {ex.Message}");
            return Empty;
        }
    }

    public Vector4 Sample(int z, float u, float v)
    {
        z = Math.Clamp(z, 0, Depth - 1);
        float fx = Math.Clamp(u, 0f, 1f) * (CellCount - 1);
        float fy = Math.Clamp(v, 0f, 1f) * (CellCount - 1);
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
        int x1 = Math.Min(x0 + 1, CellCount - 1), y1 = Math.Min(y0 + 1, CellCount - 1);
        float tx = fx - x0, ty = fy - y0;

        Vector4 c00 = _cells[z, y0, x0], c10 = _cells[z, y0, x1];
        Vector4 c01 = _cells[z, y1, x0], c11 = _cells[z, y1, x1];
        return Vector4.Lerp(Vector4.Lerp(c00, c10, tx), Vector4.Lerp(c01, c11, tx), ty);
    }
}
