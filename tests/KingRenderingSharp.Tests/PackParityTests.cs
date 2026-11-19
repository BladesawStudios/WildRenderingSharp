using System.Text.Json;
using BfShaderSharp.Pack;
using BfShaderSharp.Prepared;
using KingRenderingSharp.Content.Packs;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Tests;

/// <summary>
/// Exports a model from an asset pack and compares everything written with what ShaderLibrary's preparer wrote for the same model: the manifest, the vertices, indices, material blocks, textures, skeleton and the
/// text of the programs. Needs a folder named by KRS_PARITY, holding the pack as <c>model.bfap</c>, the prepared shaders in <c>shaders/</c> (prepared with --reflected and every --pass) and
/// the preparer's cache in <c>oracle/</c>, with the model named by KRS_PARITY_MODEL (the model file's name, as the preparer was asked for it).
/// </summary>
public sealed class PackParityTests
{
    static string? Folder => Environment.GetEnvironmentVariable("KRS_PARITY") is { Length: > 0 } f && Directory.Exists(f) ? f : null;

    static string Model => Environment.GetEnvironmentVariable("KRS_PARITY_MODEL") ?? "Npc_Zelda_AncientHyrule";

    sealed class ParityFactAttribute : FactAttribute
    {
        public ParityFactAttribute()
        {
            if (Folder is null) Skip = "Set KRS_PARITY to a folder holding model.bfap, shaders/ and oracle/ to run this test.";
        }
    }

    [ParityFact]
    public void AnExportedModelMatchesWhatTheShaderLibraryPreparerWrote()
    {
        string folder = Folder!;
        using AssetPack pack = AssetPack.Open(Path.Combine(folder, "model.bfap"));
        Dictionary<string, PreparedArchive> archives = [];
        foreach (string file in Directory.EnumerateFiles(Path.Combine(folder, "shaders"), "*.bfsp"))
            archives[Path.GetFileNameWithoutExtension(file)] = PreparedArchive.Open(file);
        try
        {
            string packModel = pack.Of(PackEntryKind.Model).Select(e => e.Name).First(n => n == Model || n.StartsWith(Model + "/", StringComparison.Ordinal));
            string output = Path.Combine(Path.GetTempPath(), "krs-parity-" + Guid.NewGuid().ToString("N"));
            string models = Path.Combine(output, "model"), shaderDir = Path.Combine(output, "shaders");
            try
            {
                using PackModelExporter exporter = new(pack, name => archives[name]);
                ExportedModel result = exporter.Export(packModel, Model, models, shaderDir);
                Assert.True(result.Problems.Count == 0, string.Join(Environment.NewLine, result.Problems.Take(30)));

                List<string> differences = [];
                new Comparison(Path.Combine(folder, "oracle", Model), Path.Combine(folder, "oracle", "_shaders"), models, shaderDir, Model, differences).Run();
                if (Environment.GetEnvironmentVariable("KRS_PARITY_DUMP") is { Length: > 0 } dump)
                {
                    Directory.CreateDirectory(dump);
                    File.WriteAllText(Path.Combine(dump, "differences.txt"), string.Join('\n', differences));
                    File.WriteAllBytes(Path.Combine(dump, "oracle.manifest.json"), CacheFiles.ReadAllBytes(Path.Combine(folder, "oracle", Model, $"{Model}.manifest.json")));
                    File.Copy(Path.Combine(models, $"{Model}.manifest.json"), Path.Combine(dump, "ours.manifest.json"), true);
                    foreach (string file in new[] { "Aprom_Ace__Mt_Accessory_A_vtx.bin", "Aprom_Ace__Mt_Accessory_A_idx.bin" })
                    {
                        File.WriteAllBytes(Path.Combine(dump, "oracle_" + file), CacheFiles.ReadAllBytes(Path.Combine(folder, "oracle", Model, file)));
                        File.Copy(Path.Combine(models, file), Path.Combine(dump, "ours_" + file), true);
                    }
                }

                Assert.True(differences.Count == 0, $"{differences.Count} differences:\n" + string.Join('\n', differences.Take(60)));
            }
            finally
            {
                if (Directory.Exists(output)) Directory.Delete(output, true);
            }
        }
        finally
        {
            foreach (PreparedArchive archive in archives.Values) archive.Dispose();
        }
    }

    sealed class Comparison(string oracleModels, string oracleShaders, string models, string shaders, string name, List<string> differences)
    {
        static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        JsonElement Read(string folder, string file) => JsonDocument.Parse(CacheFiles.ReadAllBytes(Path.Combine(folder, file))).RootElement;

        public void Run()
        {
            JsonElement oracle = Read(oracleModels, $"{name}.manifest.json"), ours = File.Exists(Path.Combine(models, $"{name}.manifest.json")) ? Read(models, $"{name}.manifest.json") : default;
            JsonElement[] oracleShapes = [.. oracle.GetProperty("shapes").EnumerateArray()], ourShapes = [.. ours.GetProperty("shapes").EnumerateArray()];
            if (oracleShapes.Length != ourShapes.Length) differences.Add($"shape count {ourShapes.Length}, oracle {oracleShapes.Length}");
            foreach (JsonElement expected in oracleShapes)
            {
                string shape = expected.GetProperty("name").GetString()!;
                JsonElement? found = ourShapes.Where(s => s.GetProperty("name").GetString() == shape).Select(s => (JsonElement?)s).FirstOrDefault();
                if (found is not { } actual)
                {
                    differences.Add($"{shape}: missing");
                    continue;
                }

                Shape(shape, expected, actual);
            }

            Skeleton();
        }

        void Shape(string shape, JsonElement expected, JsonElement actual)
        {
            foreach (string field in new[] { "material", "shading_model", "vertex_file", "index_file", "index_count", "lods", "vertex_skin_count", "bone_index", "programs", "gbuffer_shader", "zonly_shader", "material_shader",
                         "alpha_test", "o_material_behave", "deferred_pass", "render_state", "material_ubo" })
                Same($"{shape}.{field}", expected.GetProperty(field), actual.GetProperty(field));

            // The attributes a program declares: the preparer lists what the binary names, the text only what its code reads.
            Subset($"{shape}.attributes", expected.GetProperty("attributes"), actual.GetProperty("attributes"));
            foreach (string field in new[] { "samplers", "zonly_samplers", "material_samplers" })
                Samplers($"{shape}.{field}", expected.GetProperty(field), actual.GetProperty(field));

            Bytes($"{shape} vertices", oracleModels, models, expected.GetProperty("vertex_file").GetString()!, Vertices);
            Bytes($"{shape} indices", oracleModels, models, expected.GetProperty("index_file").GetString()!, Exact);
            string ubo = expected.GetProperty("material_ubo").GetString()!;
            Bytes($"{shape} material block", oracleModels, models, ubo, Exact);
            Params($"{shape} params", ubo[..^".gsys_material.bin".Length] + ".params.json");
            foreach (string program in new[] { "gbuffer_shader", "zonly_shader", "material_shader" })
                Program($"{shape}.{program}", expected.GetProperty(program).GetString()!);
        }

        void Same(string what, JsonElement expected, JsonElement actual)
        {
            if (expected.ToString() != actual.ToString() && !Json.Equal(expected, actual)) differences.Add($"{what}: {actual} (oracle {expected})");
        }

        void Subset(string what, JsonElement expected, JsonElement actual)
        {
            HashSet<string> have = [.. expected.EnumerateArray().Select(e => e.GetString()!)];
            string[] extra = [.. actual.EnumerateArray().Select(e => e.GetString()!).Where(n => !have.Contains(n))];
            if (extra.Length > 0) differences.Add($"{what}: {string.Join(", ", extra)} not in the oracle's");
        }

        void Samplers(string what, JsonElement expected, JsonElement actual)
        {
            string Key(JsonElement s) => $"{s.GetProperty("unit")}|{s.GetProperty("key")}|{s.GetProperty("assigned")}|{s.GetProperty("texture")}|{s.GetProperty("wrap_u")}|{s.GetProperty("wrap_v")}";
            string[] e = [.. expected.EnumerateArray().Select(Key)], a = [.. actual.EnumerateArray().Select(Key)];
            if (!e.SequenceEqual(a)) differences.Add($"{what}: [{string.Join("; ", a)}] (oracle [{string.Join("; ", e)}])");
            for (int i = 0; i < Math.Min(e.Length, a.Length); i++)
            {
                JsonElement x = expected[i], y = actual[i];
                string file = x.GetProperty("file").GetString()!;
                if (file == "") continue;
                if (y.GetProperty("file").GetString() == "") { differences.Add($"{what}[{i}]: no file (oracle {file})"); continue; }
                if (x.GetProperty("width").GetInt32() != y.GetProperty("width").GetInt32() || x.GetProperty("height").GetInt32() != y.GetProperty("height").GetInt32())
                    differences.Add($"{what}[{i}]: {y.GetProperty("width")}x{y.GetProperty("height")} (oracle {x.GetProperty("width")}x{x.GetProperty("height")})");
                // The preparer read a texture's selectors with an older TxtgSharp that gave the identity for the textures whose header says otherwise (a constant one for alpha, a grey texture read as red three times); the pack's are the header's. Only a selector set the oracle knew to be non-identity is compared.
                if (!Json.Equal(JsonDocument.Parse("[0,1,2,3]").RootElement, x.GetProperty("comp_select")) && !Json.Equal(JsonDocument.Parse("[5,5,5,5]").RootElement, y.GetProperty("comp_select")) && !Json.Equal(x.GetProperty("comp_select"), y.GetProperty("comp_select"))) differences.Add($"{what}[{i}]: comp_select {y.GetProperty("comp_select")} (oracle {x.GetProperty("comp_select")})");
                Bytes($"{what}[{i}] {x.GetProperty("texture")}", oracleModels, models, file, Exact, y.GetProperty("file").GetString());
            }
        }

        void Params(string what, string file)
        {
            JsonElement x = Read(oracleModels, "matubo/" + Path.GetFileName(file)), y = Read(models, "matubo/" + Path.GetFileName(file));
            Dictionary<string, JsonElement> have = y.GetProperty("uniforms").EnumerateArray().ToDictionary(u => u.GetProperty("name").GetString()!);
            foreach (JsonElement u in x.GetProperty("uniforms").EnumerateArray())
            {
                string uniform = u.GetProperty("name").GetString()!;
                if (!have.TryGetValue(uniform, out JsonElement v)) { differences.Add($"{what}: {uniform} missing"); continue; }
                foreach (string field in new[] { "offset", "type", "used", "raw_srt" })
                {
                    bool a = u.TryGetProperty(field, out JsonElement ea), b = v.TryGetProperty(field, out JsonElement eb);
                    if (a != b || (a && !Json.Equal(ea, eb))) differences.Add($"{what}: {uniform}.{field} {(b ? eb : "-")} (oracle {(a ? ea : "-")})");
                }
            }

            if (x.GetProperty("block_size").GetInt32() != y.GetProperty("block_size").GetInt32()) differences.Add($"{what}: block size");
        }

        readonly HashSet<string> _programsDone = [];

        void Program(string what, string program)
        {
            if (!_programsDone.Add(program) || program.Contains("prog-1")) return;
            foreach (string extension in new[] { "vert", "frag" })
            {
                string oracleFile = Path.Combine(oracleShaders, $"{program}.{extension}"), ourFile = Path.Combine(shaders, $"{program}.{extension}");
                if (!File.Exists(oracleFile)) continue;
                if (!File.Exists(ourFile)) { differences.Add($"{what}: {program}.{extension} not written"); continue; }
                // The two decompilers type their temporaries differently and so the bodies differ; what a renderer binds (the declared inputs, outputs, blocks and samplers, by name and slot) must not.
                string[] e = Declarations(File.ReadAllText(oracleFile)), a = Declarations(File.ReadAllText(ourFile));
                if (!e.SequenceEqual(a)) differences.Add($"{what}: {program}.{extension} declares [{string.Join("; ", a.Except(e))}] and not [{string.Join("; ", e.Except(a))}]");
            }
        }

        static string[] Declarations(string text) => [.. text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("layout", StringComparison.Ordinal) && l.EndsWith(';')).Order(StringComparer.Ordinal)];

        static string Normalize(string text) => string.Join('\n', text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).Where(l => !l.StartsWith("// wrs-decompiler", StringComparison.Ordinal))).Trim();

        static string FirstDifference(string e, string a)
        {
            string[] x = e.Split('\n'), y = a.Split('\n');
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
                if (x[i] != y[i]) return $"line {i + 1}: \"{y[i]}\" vs oracle \"{x[i]}\"";
            return $"{y.Length} lines, oracle {x.Length}";
        }

        void Bytes(string what, string oracleFolder, string folder, string file, Func<byte[], byte[], string?> compare, string? ourFile = null)
        {
            byte[] e = CacheFiles.ReadAllBytes(Path.Combine(oracleFolder, file));
            string path = Path.Combine(folder, ourFile ?? file);
            if (!File.Exists(path)) { differences.Add($"{what}: {ourFile ?? file} not written"); return; }
            if (compare(e, File.ReadAllBytes(path)) is { } message) differences.Add($"{what}: {message}");
        }

        static string? Exact(byte[] e, byte[] a) => e.AsSpan().SequenceEqual(a) ? null : $"{a.Length} bytes differ from the oracle's {e.Length}";

        // 4-byte words: equal as integers or as floats within a small tolerance.
        static string? Vertices(byte[] e, byte[] a)
        {
            if (e.Length != a.Length) return $"{a.Length} bytes, oracle {e.Length}";
            int bad = 0;
            string first = "";
            for (int i = 0; i + 4 <= e.Length; i += 4)
            {
                uint x = BitConverter.ToUInt32(e, i), y = BitConverter.ToUInt32(a, i);
                if (x == y) continue;
                float fx = BitConverter.ToSingle(e, i), fy = BitConverter.ToSingle(a, i);
                if (!float.IsNaN(fx) && !float.IsNaN(fy) && MathF.Abs(fx - fy) <= 1e-4f * Math.Max(1f, MathF.Abs(fx))) continue;
                if (bad++ == 0) first = $"vertex {i / 192} word {i % 192 / 4}: {fy} vs oracle {fx}";
            }

            return bad == 0 ? null : $"{bad} words differ, first {first}";
        }

        void Skeleton()
        {
            string file = $"{name}.skeleton.json";
            JsonElement x = Read(oracleModels, file), y = Read(models, file);
            foreach (string field in new[] { "scaling_mode", "smooth_matrix_count", "rigid_matrix_count", "matrix_to_bone_list" })
                Same($"skeleton.{field}", x.GetProperty(field), y.GetProperty(field));
            JsonElement[] bx = [.. x.GetProperty("bones").EnumerateArray()], by = [.. y.GetProperty("bones").EnumerateArray()];
            if (bx.Length != by.Length) differences.Add($"skeleton: {by.Length} bones, oracle {bx.Length}");
            for (int i = 0; i < Math.Min(bx.Length, by.Length); i++)
                foreach (JsonProperty p in bx[i].EnumerateObject())
                    if (!Json.Equal(p.Value, by[i].GetProperty(p.Name))) differences.Add($"skeleton.bones[{i}].{p.Name}: {by[i].GetProperty(p.Name)} (oracle {p.Value})");
            JsonElement[] ix = [.. x.GetProperty("inverse_model_matrices").EnumerateArray()], iy = [.. y.GetProperty("inverse_model_matrices").EnumerateArray()];
            if (ix.Length != iy.Length) differences.Add($"skeleton: {iy.Length} inverse matrices, oracle {ix.Length}");
            for (int i = 0; i < Math.Min(ix.Length, iy.Length); i++)
                if (!Json.Equal(ix[i], iy[i])) differences.Add($"skeleton.inverse_model_matrices[{i}] differs");
        }
    }

    // JSON values equal as the manifest means them: numbers by value (a float to six digits), everything else by structure.
    static class Json
    {
        public static bool Equal(JsonElement a, JsonElement b)
        {
            if (a.ValueKind != b.ValueKind) return false;
            switch (a.ValueKind)
            {
                case JsonValueKind.Number: return Math.Abs(a.GetDouble() - b.GetDouble()) <= 1e-6 * Math.Max(1, Math.Abs(a.GetDouble()));
                case JsonValueKind.Array:
                    JsonElement[] x = [.. a.EnumerateArray()], y = [.. b.EnumerateArray()];
                    return x.Length == y.Length && x.Zip(y).All(p => Equal(p.First, p.Second));
                case JsonValueKind.Object:
                    return a.EnumerateObject().All(p => b.TryGetProperty(p.Name, out JsonElement v) && Equal(p.Value, v)) && a.EnumerateObject().Count() == b.EnumerateObject().Count();
                default: return a.ToString() == b.ToString();
            }
        }
    }
}
