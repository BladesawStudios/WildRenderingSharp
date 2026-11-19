using KingRenderingSharp.Content.Packs;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Packing;

/// <summary>Imports the deferred resolve passes through a pack: the system model's shapes are the passes, each with the material block it lights with and the program its material pass runs.</summary>
/// <remarks>The programs are written as <c>deferred_&lt;pass&gt;_prog&lt;n&gt;_extracted.vert</c> and <c>.frag</c> into the shaders folder and the blocks as <c>&lt;pass&gt;.gsys_material.bin</c>, which is where the renderer's deferred passes ask for them.</remarks>
static class DeferredExporter
{
    // The system model of each game: Tears of the Kingdom's deferred model is one of its file's models, Breath of the Wild's is the file's only one.
    public static string ModelOf(string romfs) => IsBotw(romfs) ? "SystemModel" : "SystemModel.DeferredMain";

    // A Tears of the Kingdom dump keeps its packs zstd compressed, a Breath of the Wild dump does not.
    public static bool IsBotw(string romfs) => File.Exists(Path.Combine(romfs, "Pack", "Bootup_Graphics.pack"));

    public static int Export(string romfs, SystemFolders layout, Action<string> log)
    {
        string work = Path.Combine(Path.GetTempPath(), "krs-deferred-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string pack = Path.Combine(work, "model.bfap"), shaders = Path.Combine(work, "shaders");
            PreparedPackModel prepared = PackPreparer.Prepare(romfs, ModelOf(romfs), pack, shaders, null, false);
            foreach (string problem in prepared.Problems) log("[deferred] " + problem);
            using ModelPack opened = new(pack, shaders, ModelOf(romfs));
            Directory.CreateDirectory(layout.Shaders);
            Directory.CreateDirectory(layout.DeferredMaterials);
            int passes = 0;
            foreach (var shape in opened.Source.Manifest.Shapes)
            {
                if (!opened.Source.Has(shape.MaterialFile)) continue;
                File.WriteAllBytes(Path.Combine(layout.DeferredMaterials, $"{shape.Name}.gsys_material.bin"), opened.Source.Bytes(shape.MaterialFile));
                // A pass with no program of its own has the block only.
                string program = shape.MaterialShader;
                int at = program.LastIndexOf("_prog", StringComparison.Ordinal);
                if (at >= 0)
                {
                    string target = $"deferred_{shape.Name}_prog{program[(at + "_prog".Length)..].Replace("_extracted", "")}";
                    foreach (string extension in new[] { "vert", "frag" })
                        if (opened.Programs.Exists($"{program}.{extension}"))
                            File.WriteAllBytes(Path.Combine(layout.Shaders, $"{target}_extracted.{extension}"), opened.Programs.ReadBytes($"{program}.{extension}"));
                }

                passes++;
            }

            log($"{passes} deferred passes written");
            return passes;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { }
        }
    }
}
