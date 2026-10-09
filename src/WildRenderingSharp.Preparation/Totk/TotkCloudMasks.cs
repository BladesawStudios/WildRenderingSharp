using BntxSharp;
using SarcLibrary;
using WildRenderingSharp.Profiles.Totk.Sky;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>
/// The three 512x512 masks the cloud dome samples, read from the BNTX inside <c>collect.genvres</c> in
/// <c>Env/GameScene.Nin_NX_NVN.genvb.zs</c>. The game registers them into the cloud's texture table in this order, and
/// <c>CloudParam*</c> picks entries by that index (base 0, noise 1, blended base 2).
/// </summary>
public static class TotkCloudMasks
{
    public const int Size = 512;
    const int BntxOffset = 0x1000;
    const string Archive = "Env/GameScene.Nin_NX_NVN.genvb.zs";

    static readonly string[] SlotTextures = ["cloudtexture03", "cloudtexture02", "cloudtexture04"];

    public static bool IsInstalled(string systemTexturesDirectory) =>
        SlotTextures.Select((_, slot) => Path.Combine(systemTexturesDirectory, CloudDomePass.MaskFileName(slot) + ".r8")).All(File.Exists);

    /// <summary>The red channel of each slot's top mip, one byte per texel.</summary>
    public static IReadOnlyList<byte[]> ReadMasks(IRomAccess rom)
    {
        var sarc = Sarc.FromBinary(new ArraySegment<byte>(rom.ReadAllBytesNested(Archive).ToArray()));
        var entry = sarc.FirstOrDefault(e => e.Key.EndsWith("collect.genvres", StringComparison.Ordinal));
        if (entry.Key is null)
            throw new FileNotFoundException($"'{Archive}' has no collect.genvres entry.");

        byte[] collect = entry.Value.ToArray();
        var bntx = BntxFile.Load(collect.AsSpan(BntxOffset));

        return SlotTextures.Select(name =>
        {
            var texture = bntx.Textures.FirstOrDefault(t => t.Name == name)
                ?? throw new InvalidDataException($"collect.genvres has no texture '{name}'.");
            if (texture.Width != Size || texture.Height != Size)
                throw new InvalidDataException($"'{name}' is {texture.Width}x{texture.Height}, expected {Size}x{Size}.");

            byte[] rgba = texture.ToRgba8();
            var red = new byte[Size * Size];
            for (int i = 0; i < red.Length; i++)
                red[i] = rgba[i * 4];
            return red;
        }).ToList();
    }

    public static void Install(IRomAccess rom, string systemTexturesDirectory)
    {
        Directory.CreateDirectory(systemTexturesDirectory);
        var masks = ReadMasks(rom);
        for (int slot = 0; slot < masks.Count; slot++)
        {
            string name = CloudDomePass.MaskFileName(slot);
            File.WriteAllBytes(Path.Combine(systemTexturesDirectory, name + ".r8"), masks[slot]);
            File.WriteAllText(Path.Combine(systemTexturesDirectory, name + ".dims.txt"), $"{Size} {Size} 1");
        }
    }
}
