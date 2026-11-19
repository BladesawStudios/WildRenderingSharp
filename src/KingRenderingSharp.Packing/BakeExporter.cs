using System.Globalization;
using System.Text.Json;
using BfShaderSharp.Assets;
using BfShaderSharp.Pack;
using KingRenderingSharp.Content.Packs;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Packing;

/// <summary>Writes the baked lighting a pack holds as the files the renderer's bake library reads: <c>index.bin</c>, and for each tile its <c>.json</c> with the atlas textures beside it.</summary>
internal static class BakeExporter
{
    private const string TilePrefix = "Bake/tile/";

    /// <summary>Returns the number of tiles written; the index is written whenever the pack has one.</summary>
    public static int Export(AssetPack pack, string directory)
    {
        if (pack.GameData(BakeImporter.IndexEntry) is not { } index) return 0;
        Directory.CreateDirectory(directory);
        AtomicFile.Write(Path.Combine(directory, "index.bin"), temp => File.WriteAllBytes(temp, index));
        using PackTextureFiles textures = new(pack);
        int written = 0;
        foreach (PackEntry entry in pack.Entries.Where(e => e.Kind == PackEntryKind.GameData && e.Name.StartsWith(TilePrefix, StringComparison.Ordinal)))
        {
            WriteTile(pack, textures, directory, entry.Name[TilePrefix.Length..], new RecordReader(pack.Bytes(entry.Key)));
            written++;
        }

        return written;
    }

    private static void WriteTile(AssetPack pack, PackTextureFiles textures, string directory, string tile, RecordReader reader)
    {
        List<string> names = reader.List(1, r => r.String());
        List<(string Name, string File, string Format, int Width, int Height)> files = [];
        foreach (string name in names)
        {
            PackTextureFiles.File? texture = textures.Read(name);
            if (texture is null) throw new InvalidDataException($"{tile}: texture {name} cannot be exported");
            string file = $"{name}_{texture.Width}x{texture.Height}_{texture.Format}.bin";
            File.WriteAllBytes(Path.Combine(directory, file), texture.Bytes);
            files.Add((name, file, texture.Format, texture.Width, texture.Height));
        }

        AtomicFile.Write(Path.Combine(directory, tile + ".json"), temp =>
        {
            using FileStream stream = File.Create(temp);
            using Utf8JsonWriter w = new(stream);
            w.WriteStartObject();
            w.WriteStartArray("textures");
            foreach (var t in files)
            {
                w.WriteStartObject();
                w.WriteString("name", t.Name);
                w.WriteString("file", t.File);
                w.WriteString("format", t.Format);
                w.WriteNumber("width", t.Width);
                w.WriteNumber("height", t.Height);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteStartObject("actors");
            int actors = reader.Count();
            for (int i = 0; i < actors; i++)
            {
                ulong hash = (ulong)reader.U32() << 32 | reader.U32();
                w.WriteStartObject(hash.ToString(CultureInfo.InvariantCulture));
                w.WriteString("model", reader.String());
                w.WriteNumber("count", reader.I32());
                w.WriteStartArray("materials");
                int materials = reader.Count();
                for (int m = 0; m < materials; m++)
                {
                    w.WriteStartObject();
                    w.WriteString("name", reader.String());
                    w.WriteNumber("index", reader.I32());
                    w.WriteNumber("texture", reader.I32());
                    w.WriteStartArray("st");
                    for (int f = 0; f < 4; f++) w.WriteNumberValue(reader.F32());
                    w.WriteEndArray();
                    w.WriteEndObject();
                }

                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WriteEndObject();
            w.WriteEndObject();
        });
    }
}
