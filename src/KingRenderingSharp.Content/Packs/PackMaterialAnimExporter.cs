using System.Text.Json;
using System.Text.Json.Nodes;
using BfShaderSharp.Errors;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;

namespace KingRenderingSharp.Content.Packs;

/// <summary>Writes the material animations of an asset pack as the <c>Model.Anim.matanim.json</c> (shader parameters, colours and texture transforms) and <c>Model.Anim.texpat.json</c> (texture patterns) files the loader reads.</summary>
/// <remarks>
/// A material animation of the Switch formats holds both: its parameter infos become a matanim file and its pattern infos a texpat file, each only when the animation has any.
/// A parameter's curves are the item's float curves from the info's first curve and then its integer ones, and its constants are written as curves that hold one value. A curve's
/// destination is its data offset inside the parameter; the loader adds the parameter's own offset in the material block. A pattern's textures are written beside the model as the
/// material textures are, and each pattern names the shader samplers the material assigns its sampler to.
/// </remarks>
sealed class PackMaterialAnimExporter(AssetPack pack, PackTextureFiles textures)
{
    const byte TexSrt = 30, TexSrtEx = 31;

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public int Export(IReadOnlyList<string> stems, ModelRecord model, string modelName, string outDir, List<string> problems)
    {
        Dictionary<string, MaterialRecord> materials = [];
        foreach (MaterialRef reference in model.Materials) materials.TryAdd(reference.Name, pack.Material(reference.Material));

        int written = 0;
        foreach (PackEntry entry in pack.Of(PackEntryKind.MaterialAnim).Where(e => stems.Any(stem => e.Name.StartsWith(stem + "/", StringComparison.Ordinal))))
        {
            MaterialAnimRecord anim = pack.Record(entry.Key, MaterialAnimRecord.Read);
            string safe = PackAnimExporter.Safe(anim.Name);
            if (Parameters(anim, materials, problems) is { } parameters)
            {
                File.WriteAllText(Path.Combine(outDir, $"{modelName}.{safe}.matanim.json"), parameters.ToJsonString(Options));
                written++;
            }

            if (Patterns(anim, materials, outDir, problems) is { } patterns)
            {
                File.WriteAllText(Path.Combine(outDir, $"{modelName}.{safe}.texpat.json"), patterns.ToJsonString(Options));
                written++;
            }
        }

        return written;
    }

    JsonObject? Parameters(MaterialAnimRecord anim, Dictionary<string, MaterialRecord> materials, List<string> problems)
    {
        JsonArray entries = [];
        bool texSrt = false;
        foreach (MaterialAnimItemRecord item in anim.Items)
        {
            if (item.Params.Count == 0 || !materials.TryGetValue(item.Name, out MaterialRecord? material)) continue;
            JsonArray targets = [];
            foreach (ParamAnimInfoRecord info in item.Params)
            {
                string type = TypeName(material.Params.FirstOrDefault(p => p.Name == info.Name)?.Type);
                texSrt |= type is "TexSrt" or "TexSrtEx";
                for (int i = 0; i < info.FloatCurveCount + info.IntCurveCount; i++)
                {
                    int at = info.BeginCurve + i;
                    if (at >= item.Curves.Count) { problems.Add($"{anim.Name}/{item.Name}/{info.Name}: curve {at} is not in the item"); break; }
                    if (Target(item.Curves[at], info.Name, type, problems, $"{anim.Name}/{item.Name}/{info.Name}") is { } target) targets.Add(target);
                }

                for (int i = 0; i < info.ConstantCount && info.BeginConstant + i < item.Constants.Count; i++)
                {
                    ConstantRecord constant = item.Constants[info.BeginConstant + i];
                    targets.Add(new JsonObject
                    {
                        ["param"] = info.Name, ["param_type"] = type, ["byte_offset"] = (int)constant.DataOffset, ["constant_bits"] = constant.Value,
                        ["is_int"] = false, ["curve_type"] = 0, ["start_frame"] = 0f, ["end_frame"] = 0f, ["scale"] = 1f, ["offset"] = 0f, ["frames"] = new JsonArray(), ["keys"] = new JsonArray(),
                    });
                }
            }

            if (targets.Count > 0) entries.Add(new JsonObject { ["material"] = item.Name, ["targets"] = targets });
        }

        return entries.Count == 0 ? null : new JsonObject
        {
            ["name"] = anim.Name, ["kind"] = texSrt ? "TexSrt" : "ShaderParam", ["frame_count"] = anim.FrameCount, ["loop"] = anim.Loop, ["materials"] = entries,
        };
    }

    static JsonObject? Target(CurveRecord curve, string param, string type, List<string> problems, string where)
    {
        if (PackCurve.Read(curve, out string? reason) is not { } read)
        {
            problems.Add($"{where}: {reason}");
            return null;
        }

        bool isInt = read.Type >= 0x40;
        return new JsonObject
        {
            ["param"] = param, ["param_type"] = type, ["byte_offset"] = (int)curve.DataOffset, ["is_int"] = isInt, ["curve_type"] = read.Type,
            ["start_frame"] = curve.StartFrame, ["end_frame"] = curve.EndFrame, ["scale"] = read.Scale, ["offset"] = read.Offset,
            ["frames"] = new JsonArray([.. read.Frames.Select(f => JsonValue.Create(f))]),
            ["keys"] = new JsonArray([.. read.Rows.Select(r => (JsonNode)new JsonArray([.. r.Select(v => JsonValue.Create(v))]))]),
        };
    }

    // The ShaderParamType names the loader tells apart: the texture transforms, which are baked as a group; the rest only name the type.
    static string TypeName(byte? type) => type switch
    {
        TexSrt => "TexSrt",
        TexSrtEx => "TexSrtEx",
        null => "",
        var other => "Type" + other,
    };

    JsonObject? Patterns(MaterialAnimRecord anim, Dictionary<string, MaterialRecord> materials, string outDir, List<string> problems)
    {
        JsonArray entries = [];
        foreach (MaterialAnimItemRecord item in anim.Items)
        {
            if (item.Patterns.Count == 0 || !materials.TryGetValue(item.Name, out MaterialRecord? material)) continue;
            JsonArray samplers = [];
            foreach (PatternAnimInfoRecord info in item.Patterns)
            {
                // The shader samplers this material assigns the animated sampler to; a sampler no assignment names keeps its own name.
                string[] keys = [.. material.SamplerAssigns.Where(a => a.Value == info.Name).Select(a => a.Key)];
                if (keys.Length == 0) keys = [info.Name];
                float[] frames = [];
                int[] values = [];
                float start = 0, end = anim.FrameCount;
                if (info.CurveIndex >= 0 && info.CurveIndex < item.Curves.Count)
                {
                    CurveRecord curve = item.Curves[info.CurveIndex];
                    if (PackCurve.Read(curve, out string? reason) is { } read)
                    {
                        (frames, values, start, end) = (read.Frames, [.. read.Rows.Select(r => (int)read.Offset + (int)MathF.Round(r[0]))], curve.StartFrame, curve.EndFrame);
                    }
                    else problems.Add($"{anim.Name}/{item.Name}/{info.Name}: {reason}");
                }
                else values = [info.BeginConstant < item.Constants.Count ? (int)item.Constants[info.BeginConstant].Value : 0];

                foreach (string key in keys)
                {
                    samplers.Add(new JsonObject
                    {
                        ["sampler"] = key, ["start_frame"] = start, ["end_frame"] = end,
                        ["frames"] = new JsonArray([.. frames.Select(f => JsonValue.Create(f))]), ["values"] = new JsonArray([.. values.Select(v => JsonValue.Create(v))]),
                    });
                }
            }

            if (samplers.Count > 0) entries.Add(new JsonObject { ["material"] = item.Name, ["samplers"] = samplers });
        }

        if (entries.Count == 0) return null;
        JsonArray files = [];
        foreach (string texture in anim.TextureNames) files.Add(TextureEntry(texture, outDir, problems));
        return new JsonObject { ["name"] = anim.Name, ["frame_count"] = anim.FrameCount, ["loop"] = anim.Loop, ["textures"] = files, ["materials"] = entries };
    }

    JsonObject TextureEntry(string texture, string outDir, List<string> problems)
    {
        JsonObject entry = new() { ["texture"] = texture, ["file"] = "", ["format"] = "", ["width"] = 0, ["height"] = 0 };
        try
        {
            if (textures.Read(texture) is { } found)
            {
                string file = $"{texture}_{found.Width}x{found.Height}_{found.Format}.bin";
                string path = Path.Combine(outDir, file);
                if (!File.Exists(path)) File.WriteAllBytes(path, found.Bytes);
                entry["file"] = file;
                entry["format"] = found.Format;
                entry["width"] = found.Width;
                entry["height"] = found.Height;
                entry["comp_select"] = new JsonArray([.. found.CompSelect.Select(c => JsonValue.Create(c))]);
            }
        }
        catch (Exception ex) when (ex is BfShaderFormatException or BfShaderUnsupportedException)
        {
            problems.Add($"Texture {texture}: {ex.Message}");
        }

        return entry;
    }
}
