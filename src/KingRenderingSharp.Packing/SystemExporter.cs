using BfShaderSharp.Assets;
using BfShaderSharp.Pack;
using KingRenderingSharp.Core.Storage;
using KingRenderingSharp.Games.Totk.Sky.Clouds;

namespace KingRenderingSharp.Packing;

/// <summary>Prepares the data of a game that belongs to no model through BfShaderSharp and writes it where the renderer reads it in the cache.</summary>
/// <remarks>The programs the pack translates (agl, grass, terrain water) replace the older preparer's files only with <c>--pack-shaders</c>: their text differs from the older translator's (the declared types of temporaries, a point size written by vertex stages) and has been checked by render regression for the agl passes of the sky and compose only. The cloud dome's masks become <c>CloudMask&lt;n&gt;.tex</c> in the system textures folder, as the renderer's loader expects them.</remarks>
internal static class SystemExporter
{
    public static int Export(string romfs, CacheLayout layout, IReadOnlyCollection<string> bakeTiles, int jobs, bool packShaders, Action<string> log)
    {
        string pack = Path.Combine(Path.GetTempPath(), $"krs-system-{Guid.NewGuid():N}.bfgd");
        try
        {
            // The effect file's emitters name their textures by id, so the names are read from the file as the romfs has it before the pack is made.
            IReadOnlyList<string> effectTextures;
            using (RomfsFiles files = new(romfs)) effectTextures = EffectExporter.TextureNames(files);
            using (FileStream output = File.Create(pack))
                SystemAssetPreparer.Prepare(romfs, output, [], jobs, [], ["static"], effectTextures);
            using AssetPack opened = AssetPack.Open(pack);
            Directory.CreateDirectory(layout.SystemTextures);
            int written = 0;
            for (int slot = 0; opened.GameData(CloudMaskImporter.EntryName(slot)) is { } mask; slot++)
            {
                string name = CloudMasks.FileName(slot);
                File.WriteAllBytes(Path.Combine(layout.SystemTextures, SystemTexture.FileOf(name)), new SystemTexture(CloudMaskImporter.Size, CloudMaskImporter.Size, 1, 1, mask).ToBytes());
                written++;
            }

            log($"{written} cloud masks written to {layout.SystemTextures}");
            if (packShaders)
            {
                Directory.CreateDirectory(layout.Shaders);
                int agl = 0;
                foreach (PackEntry entry in opened.Of(PackEntryKind.GameData).Where(e => e.Name.StartsWith(AglShaderImporter.Prefix, StringComparison.Ordinal)))
                {
                    File.WriteAllBytes(Path.Combine(layout.Shaders, entry.Name[AglShaderImporter.Prefix.Length..]), opened.Bytes(entry.Key));
                    agl++;
                }

                log($"{agl} agl shader stages written to {layout.Shaders}");
                if (GrassExporter.Export(opened, layout) is { } grass) log($"grass programs written to {grass}");
            }

            log($"{TerrainWaterExporter.Export(opened, romfs, layout, packShaders, log)} terrain water files written to {layout.Shaders}");
            (int emitters, int haze) = EffectExporter.Export(opened, layout, packShaders, log);
            log($"{emitters} effect emitters and {haze} field haze levels written to {layout.Effects}");
            return 0;
        }
        finally
        {
            File.Delete(pack);
        }
    }
}
