using BfShaderSharp.Assets;
using BfShaderSharp.Pack;
using KingRenderingSharp.Core.Storage;
using KingRenderingSharp.Games.Totk.Sky.Clouds;

namespace KingRenderingSharp.Packing;

/// <summary>Prepares the data of a game that belongs to no model through BfShaderSharp and writes it where the renderer reads it in the cache.</summary>
/// <remarks>The cloud dome's masks become <c>CloudMask&lt;n&gt;.tex</c> in the system textures folder, as the renderer's loader expects them.</remarks>
internal static class SystemExporter
{
    /// <summary>Writes the system data into the cache's loose folders, for <see cref="SystemSealer"/> to fold into the pack. A step that fails costs the feature it makes and is logged; the pass that composes the picture is what the renderer cannot start without.</summary>
    public static int Export(string romfs, SystemFolders layout, Action<string> log)
    {
        List<string> failures = [];
        void Step(string name, Action step)
        {
            try
            {
                step();
            }
            catch (Exception exception)
            {
                failures.Add($"'{name}': {exception.GetBaseException().Message}");
                log($"[system] step '{name}' failed: {exception}");
            }
        }

        // Breath of the Wild has the deferred passes and nothing else that belongs to no model.
        if (DeferredExporter.IsBotw(romfs))
        {
            Step("deferred passes", () => DeferredExporter.Export(romfs, layout, log));
            return Directory.Exists(layout.DeferredMaterials) && failures.Count == 0 ? 0 : 1;
        }

        string pack = Path.Combine(Path.GetTempPath(), $"krs-system-{Guid.NewGuid():N}.bfgd");
        try
        {
            // The effect file's emitters name their textures by id, so the names are read from the file as the romfs has it before the pack is made.
            IReadOnlyList<string> effectTextures;
            using (RomfsFiles files = new(romfs)) effectTextures = EffectExporter.TextureNames(files);
            using (FileStream output = File.Create(pack))
                SystemAssetPreparer.Prepare(romfs, output, [], 0, [], ["static"], effectTextures);
            using AssetPack opened = AssetPack.Open(pack);
            Step("system textures", () => log($"{WriteSystemTextures(opened, layout)} system textures written to {layout.SystemTextures}"));
            Step("agl shaders", () => log($"{WriteAglShaders(opened, layout)} agl shader stages written to {layout.Shaders}"));
            Step("grass shaders", () => log($"{GrassExporter.Export(opened, layout)} grass files written to {layout.GrassShaders}"));
            Step("terrain, cave and depths shaders", () => log($"{ShadingExporter.Export(opened, layout)} terrain, cave and depths files written to {layout.Shaders}"));
            Step("deferred passes", () => DeferredExporter.Export(romfs, layout, log));
            Step("terrain water", () => log($"{TerrainWaterExporter.Export(opened, romfs, layout, log)} terrain water files written to {layout.Shaders}"));
            Step("effects", () =>
            {
                (int emitters, int haze) = EffectExporter.Export(opened, layout, log);
                log($"{emitters} effect emitters and {haze} field haze levels written to {layout.Effects}");
            });
            // The renderer cannot start without the HDR compose shader.
            if (!File.Exists(Path.Combine(layout.Shaders, "agl_hdr_compose.vert")))
            {
                log("[system] the system assets could not be built: " + string.Join("; ", failures));
                return 1;
            }

            return 0;
        }
        finally
        {
            File.Delete(pack);
        }
    }

    // The cloud dome's masks and the sky bodies' textures, as the files of the system textures store.
    static int WriteSystemTextures(AssetPack opened, SystemFolders layout)
    {
        Directory.CreateDirectory(layout.SystemTextures);
        int written = 0;
        for (int slot = 0; opened.GameData(CloudMaskImporter.EntryName(slot)) is { } mask; slot++)
        {
            string name = CloudMasks.FileName(slot);
            File.WriteAllBytes(Path.Combine(layout.SystemTextures, SystemTexture.FileOf(name)), new SystemTexture(CloudMaskImporter.Size, CloudMaskImporter.Size, 1, 1, mask).ToBytes());
            written++;
        }

        foreach (PackEntry entry in opened.Of(PackEntryKind.GameData).Where(e => e.Name.StartsWith(SystemTextureImporter.Group + "/", StringComparison.Ordinal)))
        {
            File.WriteAllBytes(Path.Combine(layout.SystemTextures, SystemTexture.FileOf(entry.Name[(SystemTextureImporter.Group.Length + 1)..])), opened.Bytes(entry.Key));
            written++;
        }

        return written;
    }

    static int WriteAglShaders(AssetPack opened, SystemFolders layout)
    {
        Directory.CreateDirectory(layout.Shaders);
        int written = 0;
        foreach (PackEntry entry in opened.Of(PackEntryKind.GameData).Where(e => e.Name.StartsWith(AglShaderImporter.Prefix, StringComparison.Ordinal)))
        {
            File.WriteAllBytes(Path.Combine(layout.Shaders, entry.Name[AglShaderImporter.Prefix.Length..]), opened.Bytes(entry.Key));
            written++;
        }

        return written;
    }
}
