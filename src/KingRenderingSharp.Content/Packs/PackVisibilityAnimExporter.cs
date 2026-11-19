using System.Text.Json;
using System.Text.Json.Nodes;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;

namespace KingRenderingSharp.Content.Packs;

/// <summary>Writes the visibility animations of an asset pack as <c>Model.Anim.visanim.json</c> and its shape animations as <c>Model.Anim.shapeanim.json</c>.</summary>
/// <remarks>
/// A visibility animation is for bones or for materials (its kind), and lists each name it covers with its starting visibility and, when a curve drives it, the curve's frames and 0 or 1
/// keys. A curve belongs to the name its data offset indexes; a file whose offsets do not index the names gives its curves to the names in order. A shape animation lists, for each shape,
/// its key shapes and the weight curve of each. Both curves are written as the loader evaluates them: <c>curve_type</c>, <c>start_frame</c>, <c>end_frame</c>, <c>frames</c> and a row of <c>keys</c> for each frame.
/// </remarks>
static class PackVisibilityAnimExporter
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static int Export(AssetPack pack, IReadOnlyList<string> stems, string modelName, string outDir, List<string> problems)
    {
        int written = 0;
        foreach (PackEntry entry in Entries(pack, PackEntryKind.VisibilityAnim, stems))
        {
            VisibilityAnimRecord anim = pack.Record(entry.Key, VisibilityAnimRecord.Read);
            File.WriteAllText(Path.Combine(outDir, $"{modelName}.{PackAnimExporter.Safe(anim.Name)}.visanim.json"), Visibility(anim, problems).ToJsonString(Options));
            written++;
        }

        foreach (PackEntry entry in Entries(pack, PackEntryKind.ShapeAnim, stems))
        {
            ShapeAnimRecord anim = pack.Record(entry.Key, ShapeAnimRecord.Read);
            File.WriteAllText(Path.Combine(outDir, $"{modelName}.{PackAnimExporter.Safe(anim.Name)}.shapeanim.json"), Shape(anim, problems).ToJsonString(Options));
            written++;
        }

        return written;
    }

    static IEnumerable<PackEntry> Entries(AssetPack pack, PackEntryKind kind, IReadOnlyList<string> stems) =>
        pack.Of(kind).Where(e => stems.Any(stem => e.Name.StartsWith(stem + "/", StringComparison.Ordinal)));

    static JsonObject Visibility(VisibilityAnimRecord anim, List<string> problems)
    {
        // The curve of each name: by the data offset when every offset is a distinct name index, otherwise in order.
        bool indexed = anim.Curves.Count > 0 && anim.Curves.All(c => c.DataOffset < anim.Names.Count) && anim.Curves.Select(c => c.DataOffset).Distinct().Count() == anim.Curves.Count;
        JsonArray entries = [];
        for (int i = 0; i < anim.Names.Count; i++)
        {
            int curveAt = indexed ? anim.Curves.ToList().FindIndex(c => c.DataOffset == i) : i < anim.Curves.Count ? i : -1;
            entries.Add(new JsonObject
            {
                ["name"] = anim.Names[i], ["base"] = i < anim.Base.Length && anim.Base[i] != 0,
                ["curve"] = curveAt < 0 ? null : Curve(anim.Curves[curveAt], $"{anim.Name}/{anim.Names[i]}", problems),
            });
        }

        return new JsonObject { ["name"] = anim.Name, ["kind"] = anim.Kind == 0 ? "Bone" : "Material", ["frame_count"] = anim.FrameCount, ["loop"] = anim.Loop, ["entries"] = entries };
    }

    static JsonObject Shape(ShapeAnimRecord anim, List<string> problems)
    {
        JsonArray shapes = [];
        foreach (VertexShapeAnimRecord shape in anim.Shapes)
        {
            JsonArray keys = [];
            foreach (KeyShapeInfoRecord key in shape.KeyShapes)
            {
                keys.Add(new JsonObject
                {
                    ["name"] = key.Name,
                    ["curve"] = key.CurveIndex >= 0 && key.CurveIndex < shape.Curves.Count ? Curve(shape.Curves[key.CurveIndex], $"{anim.Name}/{shape.Name}/{key.Name}", problems) : null,
                });
            }

            shapes.Add(new JsonObject { ["shape"] = shape.Name, ["base"] = new JsonArray([.. shape.Base.Select(b => JsonValue.Create(b))]), ["key_shapes"] = keys });
        }

        return new JsonObject { ["name"] = anim.Name, ["frame_count"] = anim.FrameCount, ["loop"] = anim.Loop, ["shapes"] = shapes };
    }

    static JsonObject? Curve(CurveRecord curve, string where, List<string> problems)
    {
        if (PackCurve.Read(curve, out string? reason) is not { } read)
        {
            problems.Add($"{where}: {reason}");
            return null;
        }

        return new JsonObject
        {
            ["curve_type"] = read.Type, ["start_frame"] = curve.StartFrame, ["end_frame"] = curve.EndFrame, ["scale"] = read.Scale, ["offset"] = read.Offset,
            ["frames"] = new JsonArray([.. read.Frames.Select(f => JsonValue.Create(f))]),
            ["keys"] = new JsonArray([.. read.Rows.Select(r => (JsonNode)new JsonArray([.. r.Select(v => JsonValue.Create(v))]))]),
        };
    }
}
