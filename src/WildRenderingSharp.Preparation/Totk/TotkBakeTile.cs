using System.Globalization;
using System.Text.Json;
using BymlLibrary;
using ShaderLibrary.CompileTool;
using WildRenderingSharp.Rom;
using WildRenderingSharp.Storage;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>Exports one bake tile's atlas textures and per-actor material regions to JSON.</summary>
public static class TotkBakeTile
{
    sealed record Texture(string Name, string File, string Format, int Width, int Height);

    sealed record Material(string Name, int Index, int Texture, float[] St);

    sealed record Actor(string Model, int Count, List<Material> Materials);

    public static void Export(IRomAccess rom, string tile, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var root = TotkBake.ReadTile(rom, TotkBake.TilePath(tile));

        var textures = new List<Texture>();
        var textureIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var actors = new SortedDictionary<ulong, Actor>();

        foreach (Byml element in root.GetMap()["DataElements"].GetArray())
        {
            var map = element.GetMap();
            int[] local = ExportTextures(rom, tile, outDir, map, textures, textureIndex);
            if (!map.TryGetValue("ModelElements", out var models))
                continue;
            foreach (Byml model in models.GetArray())
            {
                var m = model.GetMap();
                if (TotkBake.ParseGuid(m["Guid"].GetString()) is { } hash && !actors.ContainsKey(hash))
                    actors[hash] = ReadActor(m, local);
            }
        }

        WriteJson(outDir, tile, textures, actors);
    }

    // Each distinct texture is exported once per tile; the returned array maps an element's local texture index to the tile's.
    static int[] ExportTextures(IRomAccess rom, string tile, string outDir, IDictionary<string, Byml> element,
        List<Texture> textures, Dictionary<string, int> textureIndex)
    {
        var names = element.TryGetValue("TextureNames", out var tn) ? tn.GetArray().Select(n => n.GetString()).ToList() : new List<string>();
        var local = new int[names.Count];
        for (int i = 0; i < names.Count; i++)
        {
            if (!textureIndex.TryGetValue(names[i], out int at))
            {
                at = -1;
                if (TotkTextures.Handle(rom, names[i]) is { } handle)
                {
                    var (texture, file) = handle.ExportMipChain(names[i], outDir);
                    at = textures.Count;
                    textures.Add(new Texture(names[i], file, texture.Format.ToString(), texture.Width, texture.Height));
                }
                else
                    Console.WriteLine($"[ExportBake] {tile}: texture not found: {names[i]}");
                textureIndex[names[i]] = at;
            }
            local[i] = at;
        }
        return local;
    }

    static Actor ReadActor(IDictionary<string, Byml> model, int[] local)
    {
        var materials = new List<Material>();
        if (model.TryGetValue("MaterialElements", out var elements))
        {
            foreach (Byml material in elements.GetArray())
            {
                var e = material.GetMap();
                int texture = e.TryGetValue("TextureIndex", out var index) ? (int)Num(index) : 0;
                var scale = e["TexcoordScale"].GetMap();
                var offset = e["TexcoordOffset"].GetMap();
                materials.Add(new Material(e["MaterialName"].GetString(), (int)Num(e["OriginalMaterialIndex"]),
                    texture >= 0 && texture < local.Length ? local[texture] : -1,
                    [Num(scale["X"]), Num(scale["Y"]), Num(offset["X"]), Num(offset["Y"])]));
            }
        }
        int count = model.TryGetValue("OriginalMaterialCount", out var original) ? (int)Num(original) : materials.Count;
        return new Actor(model.TryGetValue("ModelName", out var name) ? name.GetString() : "", count, materials);
    }

    static void WriteJson(string outDir, string tile, List<Texture> textures, SortedDictionary<ulong, Actor> actors)
    {
        AtomicFile.Write(Path.Combine(outDir, tile + ".json"), temp =>
        {
            using var stream = File.Create(temp);
            using var w = new Utf8JsonWriter(stream);
            w.WriteStartObject();
            w.WriteStartArray("textures");
            foreach (var t in textures)
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
            foreach (var (hash, actor) in actors)
                WriteActor(w, hash, actor);
            w.WriteEndObject();
            w.WriteEndObject();
        });
    }

    static void WriteActor(Utf8JsonWriter w, ulong hash, Actor actor)
    {
        w.WriteStartObject(hash.ToString(CultureInfo.InvariantCulture));
        w.WriteString("model", actor.Model);
        w.WriteNumber("count", actor.Count);
        w.WriteStartArray("materials");
        foreach (var material in actor.Materials)
        {
            w.WriteStartObject();
            w.WriteString("name", material.Name);
            w.WriteNumber("index", material.Index);
            w.WriteNumber("texture", material.Texture);
            w.WriteStartArray("st");
            foreach (float f in material.St)
                w.WriteNumberValue(f);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    static float Num(Byml b) => b.Type switch
    {
        BymlNodeType.Float => b.GetFloat(),
        BymlNodeType.Double => (float)b.GetDouble(),
        BymlNodeType.Int => b.GetInt(),
        BymlNodeType.UInt32 => b.GetUInt32(),
        BymlNodeType.Int64 => b.GetInt64(),
        BymlNodeType.UInt64 => b.GetUInt64(),
        _ => 0f,
    };
}
