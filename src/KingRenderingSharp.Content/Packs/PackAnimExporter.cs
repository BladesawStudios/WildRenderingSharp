using System.Globalization;
using System.Text;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;

namespace KingRenderingSharp.Content.Packs;

/// <summary>Writes the skeletal animations of an asset pack as the <c>Model.Anim.anim.json</c> files the animation loader reads.</summary>
/// <remarks>
/// A curve is written as the loader evaluates it: its type (the flags' curve type, 0 cubic, 0x10 linear, 0x20 baked float), the frames, and a row of keys for each frame
/// (four cubic coefficients, two linear values, or one value); a compressed cubic curve of 8 or 16-bit keys is decoded to its values and slopes and written as cubic
/// coefficients. A curve that is none of these (an integer curve, a quaternion-key curve) is left out and reported, as it drives no bone transform.
/// </remarks>
static class PackAnimExporter
{
    public static int Export(AssetPack pack, IReadOnlyList<string> stems, string modelName, string outDir, List<string> problems)
    {
        int written = 0;
        foreach (PackEntry entry in pack.Of(PackEntryKind.SkeletalAnim).Where(e => stems.Any(stem => e.Name.StartsWith(stem + "/", StringComparison.Ordinal))))
        {
            SkeletalAnimRecord anim = pack.Record(entry.Key, SkeletalAnimRecord.Read);
            string path = Path.Combine(outDir, $"{modelName}.{Safe(anim.Name)}.anim.json");
            File.WriteAllText(path, Json(anim, problems));
            written++;
        }

        return written;
    }

    internal static string Safe(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    static string Floats(IEnumerable<float> values) => string.Join(", ", values.Select(F));

    static string Json(SkeletalAnimRecord anim, List<string> problems)
    {
        StringBuilder sb = new();
        sb.AppendLine("{");
        sb.AppendLine($"  \"name\": \"{anim.Name}\",");
        sb.AppendLine($"  \"frame_count\": {anim.FrameCount},");
        sb.AppendLine($"  \"loop\": {(anim.Loop ? "true" : "false")},");
        sb.AppendLine($"  \"rotation_is_quaternion\": {((anim.Flags & 0x7000) == 0 ? "true" : "false")},");
        sb.AppendLine($"  \"scaling_mode\": {(anim.Flags >> 8) & 3},");
        sb.AppendLine("  \"bone_anims\": [");
        for (int b = 0; b < anim.Bones.Count; b++)
        {
            BoneAnimRecord bone = anim.Bones[b];
            float[] data = bone.Base.Length >= 10 ? bone.Base : [1, 1, 1, 0, 0, 0, 1, 0, 0, 0];
            sb.AppendLine("    {");
            sb.AppendLine($"      \"bone_name\": \"{bone.Name}\",");
            sb.AppendLine($"      \"use_scale\": {Bool((bone.Flags & 1u << 3) != 0)},");
            sb.AppendLine($"      \"use_rotate\": {Bool((bone.Flags & 1u << 4) != 0)},");
            sb.AppendLine($"      \"use_translate\": {Bool((bone.Flags & 1u << 5) != 0)},");
            sb.AppendLine($"      \"segment_scale_compensate\": {Bool((bone.Flags & 1u << 23) != 0)},");
            sb.AppendLine($"      \"base_scale\": [{Floats(data[0..3])}],");
            sb.AppendLine($"      \"base_translate\": [{Floats(data[7..10])}],");
            sb.AppendLine($"      \"base_rotate\": [{Floats(data[3..7])}],");
            sb.AppendLine("      \"curves\": [");
            List<string> curves = [];
            foreach (CurveRecord curve in bone.Curves)
            {
                if (Curve(curve, anim.Name, bone.Name, problems) is { } text) curves.Add(text);
            }

            sb.AppendLine(string.Join(",\n", curves));
            sb.AppendLine("      ]");
            sb.AppendLine(b == anim.Bones.Count - 1 ? "    }" : "    },");
        }

        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();

        static string Bool(bool value) => value ? "true" : "false";
    }

    static string? Curve(CurveRecord curve, string anim, string bone, List<string> problems)
    {
        if (PackCurve.Read(curve, out string? reason) is not { } read)
        {
            problems.Add($"{anim}/{bone}: {reason}");
            return null;
        }

        float[] frames = read.Frames;
        StringBuilder sb = new();
        sb.AppendLine("        {");
        sb.AppendLine($"          \"curve_type\": {read.Type},");
        sb.AppendLine($"          \"target_offset\": {curve.DataOffset},");
        sb.AppendLine($"          \"start_frame\": {F(curve.StartFrame)},");
        sb.AppendLine($"          \"end_frame\": {F(curve.EndFrame)},");
        sb.AppendLine($"          \"scale\": {F(read.Scale)},");
        sb.AppendLine($"          \"offset\": {F(read.Offset)},");
        sb.AppendLine($"          \"delta\": {F(curve.Delta)},");
        sb.AppendLine($"          \"pre_wrap\": {(curve.Flags >> 8) & 3},");
        sb.AppendLine($"          \"post_wrap\": {(curve.Flags >> 12) & 3},");
        sb.AppendLine($"          \"frames\": [{Floats(frames)}],");
        sb.AppendLine($"          \"keys\": [{read.KeysJson()}]");
        sb.Append("        }");
        return sb.ToString();
    }
}
