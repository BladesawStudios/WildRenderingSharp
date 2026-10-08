using SarcLibrary;
using WildRenderingSharp.Profiles.Totk.Sky;
using Syroot.NintenTools.NSW.Bntx;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>
/// The three 512x512 masks the cloud dome samples, read from the BNTX inside <c>collect.genvres</c> in
/// <c>Env/GameScene.Nin_NX_NVN.genvb.zs</c>. The game registers them into the cloud's texture table in this order, and
/// <c>CloudParam*</c> picks entries by that index (base 0, noise 1, blended base 2, blended noise 1).
/// </summary>
public static class TotkCloudMasks
{
    public const int Size = 512;
    const int BntxOffset = 0x1000;

    static readonly string[] SlotTextures = ["cloudtexture03", "cloudtexture02", "cloudtexture04"];

    public static bool IsInstalled(string systemTexturesDirectory) =>
        SlotTextures.Select((_, slot) => Path.Combine(systemTexturesDirectory, CloudDomePass.MaskFileName(slot) + ".r8")).All(File.Exists);

    /// <summary>The block-linear BC4 data of each slot, deswizzled to row-major 4x4 blocks.</summary>
    public static IReadOnlyList<byte[]> ReadBc4(string romfsRoot)
    {
        string path = Path.Combine(romfsRoot, "Env", "GameScene.Nin_NX_NVN.genvb.zs");
        byte[] raw = File.ReadAllBytes(path);
        byte[] genvb = TotkCommon.Zstd.IsCompressed(raw) ? TotkCommon.Totk.Zstd.Decompress(raw) : raw;
        var sarc = Sarc.FromBinary(new ArraySegment<byte>(genvb));

        var entry = sarc.FirstOrDefault(e => e.Key.EndsWith("collect.genvres", StringComparison.Ordinal));
        if (entry.Key is null)
            throw new FileNotFoundException($"'{path}' has no collect.genvres entry.");

        byte[] collect = entry.Value.ToArray();
        using var bntxStream = new MemoryStream(collect, BntxOffset, collect.Length - BntxOffset);
        var bntx = new BntxFile(bntxStream);

        return SlotTextures.Select(name =>
        {
            var texture = bntx.Textures.FirstOrDefault(t => t.Name == name)
                ?? throw new InvalidDataException($"collect.genvres has no texture '{name}'.");
            if (texture.Width != Size || texture.Height != Size)
                throw new InvalidDataException($"'{name}' is {texture.Width}x{texture.Height}, expected {Size}x{Size}.");
            return Bc4BlockLinear.Deswizzle(texture.TextureData[0][0], Size, Size);
        }).ToList();
    }

    public static void Install(string romfsRoot, string systemTexturesDirectory)
    {
        Directory.CreateDirectory(systemTexturesDirectory);
        var slots = ReadBc4(romfsRoot);
        for (int slot = 0; slot < slots.Count; slot++)
        {
            string name = CloudDomePass.MaskFileName(slot);
            File.WriteAllBytes(Path.Combine(systemTexturesDirectory, name + ".r8"), Bc4Decoder.Decode(slots[slot], Size, Size));
            File.WriteAllText(Path.Combine(systemTexturesDirectory, name + ".dims.txt"), $"{Size} {Size} 1");
        }
    }
}
