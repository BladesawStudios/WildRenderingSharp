using BfShaderSharp.Assets;
using BfShaderSharp.Pack;
using KingRenderingSharp.Core.Storage;
using KingRenderingSharp.Games.Totk.Sky.Clouds;

namespace KingRenderingSharp.Packing;

/// <summary>Prepares the data of a game that belongs to no model through BfShaderSharp and writes it where the renderer reads it in the cache.</summary>
/// <remarks>The cloud dome's masks become <c>CloudMask&lt;n&gt;.r8</c> and <c>CloudMask&lt;n&gt;.dims.txt</c> in the system textures folder, as the renderer's loader expects them.</remarks>
internal static class SystemExporter
{
    public static int Export(string romfs, CacheLayout layout, IReadOnlyCollection<string> bakeTiles, int jobs, Action<string> log)
    {
        string pack = Path.Combine(Path.GetTempPath(), $"krs-system-{Guid.NewGuid():N}.bfgd");
        try
        {
            using (FileStream output = File.Create(pack))
                SystemAssetPreparer.Prepare(romfs, output, bakeTiles, jobs);
            using AssetPack opened = AssetPack.Open(pack);
            Directory.CreateDirectory(layout.SystemTextures);
            int written = 0;
            for (int slot = 0; opened.GameData(CloudMaskImporter.EntryName(slot)) is { } mask; slot++)
            {
                string name = CloudMasks.FileName(slot);
                File.WriteAllBytes(Path.Combine(layout.SystemTextures, name + ".r8"), mask);
                File.WriteAllText(Path.Combine(layout.SystemTextures, name + ".dims.txt"), $"{CloudMaskImporter.Size} {CloudMaskImporter.Size} 1");
                written++;
            }

            log($"{written} cloud masks written to {layout.SystemTextures}");
            int tiles = BakeExporter.Export(opened, layout.Bake);
            log($"{tiles} bake tiles written to {layout.Bake}");
            return 0;
        }
        finally
        {
            File.Delete(pack);
        }
    }
}
