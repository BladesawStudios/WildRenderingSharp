using BfShaderSharp.Pack;
using KingRenderingSharp.Content.Packs;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Packing;

/// <summary>Folds the loose folders the system data was prepared into (programs, deferred material blocks, system textures, effects, grass programs) into the one pack the renderer reads, and removes them.</summary>
/// <remarks>The renderer draws from the system pack alone. The bake tiles, which are prepared on demand and later, stay where they are.</remarks>
static class SystemSealer
{
    public static int Seal(SystemFolders layout, Action<string> log)
    {
        (string Store, string Folder)[] stores =
        [
            (SystemPack.ShadersStore, layout.Shaders), (SystemPack.DeferredMaterialsStore, layout.DeferredMaterials),
            (SystemPack.SystemTexturesStore, layout.SystemTextures), (SystemPack.EffectsStore, layout.Effects), (SystemPack.GrassStore, layout.GrassShaders),
        ];
        string temporary = layout.SystemPack + ".tmp";
        int files = 0;
        using (FileStream output = File.Create(temporary))
        {
            PackWriter writer = new(output);
            foreach ((string store, string folder) in stores)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    string name = Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/');
                    writer.AddEntry(PackEntryKind.GameData, PackContentFiles.EntryName(store, name), writer.Add(File.ReadAllBytes(file)));
                    files++;
                }
            }

            writer.Finish();
        }

        File.Move(temporary, layout.SystemPack, true);
        log($"{files} system files sealed into {layout.SystemPack}");
        return 0;
    }
}
