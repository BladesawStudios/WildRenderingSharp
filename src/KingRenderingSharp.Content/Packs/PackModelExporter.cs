using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Errors;
using BfShaderSharp.Lookup;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;
using BfShaderSharp.Prepared;

namespace KingRenderingSharp.Content.Packs;

/// <summary>What exporting one model from a pack wrote.</summary>
/// <param name="Shapes">The shapes written.</param>
/// <param name="Programs">The distinct programs the shapes draw with.</param>
/// <param name="Textures">The texture files written.</param>
/// <param name="Problems">What could not be carried over, one line each: a program the shader file lacks, a texture in a format the renderer cannot upload.</param>
sealed record ExportedModel(int Shapes, int Programs, int Textures, IReadOnlyList<string> Problems);

/// <summary>Writes a model of an asset pack as the folder of files the model loader reads: the manifest, the vertex, index and material files, the textures and the skeleton, with its programs in the shared shader folder.</summary>
/// <remarks>
/// The programs come from the pack's prepared shader files, which must have been prepared with the reflected text (<c>bfshader assets ... --reflected</c>) and with every pass of the
/// renderer (<c>--pass gsys_assign_gbuffer</c> and the others): the renderer's patches read the names a stage's reflection gives, and a program that was not prepared is
/// reported as a problem and left unresolved in the manifest, which leaves its shape undrawable. A program is chosen for each pass from the material's own option choices and the
/// draw-time options of the shape: its skin weight count and the pass. Animations are not exported.
/// </remarks>
sealed partial class PackModelExporter(AssetPack pack, Func<string, PreparedArchive> shaders) : IDisposable
{
    static readonly string[] Passes = ["gbuffer", "zonly", "material"];

    // o_material_behave to the deferred pass whose materials carry that value; 1 is shared by chara_metal, clear and debug_simple and 2 by the field family.
    static readonly Dictionary<string, string> BehaveToPass = new()
    {
        ["0"] = "chara_nonmetal", ["1"] = "chara_metal", ["10"] = "chara_grossy",
        ["100"] = "chara_hair", ["101"] = "chara_skin", ["104"] = "chara_eye",
        ["105"] = "field_leaf", ["103"] = "field_water", ["2"] = "field_hybrid", ["102"] = "field_miasma",
    };

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never };

    readonly PackTextureFiles _textures = new(pack);

    /// <summary>The texture reader, shared with the animation exporters that write textures of their own.</summary>
    public PackTextureFiles Textures => _textures;
    readonly ConcurrentDictionary<(string Model, int Program), string?> _written = new();
    readonly ConcurrentDictionary<string, ProgramLookup> _lookups = new();

    public ExportedModel Export(string packModelName, string modelName, string outDir, string shaderDir)
    {
        ModelRecord model = pack.Model(packModelName) ?? throw new KeyNotFoundException($"The pack has no model {packModelName}");
        SkeletonRecord skeleton = pack.Skeleton(model.Skeleton);
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(shaderDir);
        Directory.CreateDirectory(Path.Combine(outDir, "matubo"));
        List<string> problems = [];
        if (PackSkeleton.ToJson(skeleton) is { } skeletonJson) File.WriteAllText(Path.Combine(outDir, $"{modelName}.skeleton.json"), skeletonJson);
        Matrix4x4[] world = PackSkeleton.BoneWorldMatrices(skeleton);

        Dictionary<string, string> textureFiles = [];
        HashSet<(string, int)> programs = [];
        List<Dictionary<string, object?>> shapeEntries = [];
        foreach (ShapeRef reference in model.Shapes)
        {
            ShapeRecord shape = pack.Shape(reference.Shape);
            MaterialRef materialRef = model.Materials[reference.MaterialIndex];
            MaterialRecord material = pack.Material(materialRef.Material);
            string safe = reference.Name.Replace(':', '_').Replace('/', '_');
            shapeEntries.Add(ShapeEntry(outDir, shaderDir, reference, shape, material, materialRef.Name, safe, world, textureFiles, programs, problems));
        }

        Dictionary<string, object?> manifest = new()
        {
            ["model"] = modelName,
            ["vertex_stride"] = KingVertexLayout.Stride,
            ["vertex_layout"] = KingVertexLayout.Attributes.Select(a => new Dictionary<string, object?> { ["name"] = a.Name, ["location"] = a.Location, ["offset"] = a.Offset, ["components"] = a.Components }).ToList(),
            ["shapes"] = shapeEntries,
        };
        File.WriteAllText(Path.Combine(outDir, $"{modelName}.manifest.json"), JsonSerializer.Serialize(manifest, Json));
        return new ExportedModel(shapeEntries.Count, programs.Count, textureFiles.Count, problems);
    }

    Dictionary<string, object?> ShapeEntry(string outDir, string shaderDir, ShapeRef reference, ShapeRecord shape, MaterialRecord material, string materialName, string safe,
        Matrix4x4[] world, Dictionary<string, string> textureFiles, HashSet<(string, int)> programs, List<string> problems)
    {
        Matrix4x4 rigid = shape.SkinCount == 0 && reference.BoneIndex < world.Length ? world[reference.BoneIndex] : Matrix4x4.Identity;
        File.WriteAllBytes(Path.Combine(outDir, $"{safe}_vtx.bin"), KingVertexPacker.Vertices(shape, rigid, pack.Bytes));
        (byte[] indices, List<int[]> lods) = KingVertexPacker.Indices(shape, pack.Bytes);
        File.WriteAllBytes(Path.Combine(outDir, $"{safe}_idx.bin"), indices);
        File.WriteAllText(Path.Combine(outDir, $"{safe}_options.txt"), Options(material));

        Dictionary<string, int> selected = [];
        Dictionary<string, List<Dictionary<string, object?>>> samplers = [];
        List<(string Name, int Program)> chosen = [];
        PreparedModel? preparedModel = null;
        ProgramLookup? lookup = null;
        string shading = material.ShadingModel;
        if (material.Program >= 0 || material.ShaderArchive != "")
        {
            try
            {
                PreparedArchive archive = shaders(material.ShaderArchive);
                preparedModel = archive.Model(shading);
                if (preparedModel is not null) lookup = _lookups.GetOrAdd(material.ShaderArchive + "/" + shading, _ => preparedModel.CreateLookup());
            }
            catch (KeyNotFoundException)
            {
                problems.Add($"{materialName}: no shader file for archive {material.ShaderArchive}");
            }
        }

        foreach (string pass in Passes)
        {
            int program = preparedModel is not null && lookup is not null ? Select(lookup, material, shape.SkinCount, pass) : -1;
            if (program >= 0 && preparedModel!.Glsl(program, ShaderStage.Fragment) is null && preparedModel.Glsl(program, ShaderStage.Vertex) is null)
            {
                problems.Add($"{reference.Name}: program {program} of {shading} for {pass} was not prepared");
                program = -1;
            }

            selected[pass] = program;
            if (program >= 0)
            {
                WriteProgram(shaderDir, preparedModel!, shading, program, problems);
                programs.Add((shading, program));
                chosen.Add((pass, program));
            }
            samplers[pass] = program >= 0 ? Samplers(preparedModel!, program, material, textureFiles, outDir, problems) : [];
        }

        List<(int Location, string Name)> inputs = selected["gbuffer"] >= 0 ? Inputs(Path.Combine(shaderDir, $"{shading}_prog{selected["gbuffer"]}_extracted.vert")) : [];
        inputs = [.. inputs.Where(i => KingVertexLayout.Attributes.Any(a => a.Location == i.Location))];
        string behave = ResolveOption(material, lookup, "o_material_behave");
        string materialFile = WriteMaterial(outDir, shaderDir, material, materialName, shading, selected);

        Dictionary<string, object?> entry = new()
        {
            ["name"] = reference.Name,
            ["material"] = materialName,
            ["shading_model"] = shading,
            ["vertex_file"] = $"{safe}_vtx.bin",
            ["index_file"] = $"{safe}_idx.bin",
            ["index_count"] = lods[0][1],
            ["lods"] = lods,
            ["vertex_skin_count"] = (int)shape.SkinCount,
            ["bone_index"] = (int)reference.BoneIndex,
            ["vertex_format"] = KingVertexLayout.Format(inputs.Select(i => i.Location).ToHashSet()),
            ["attributes"] = inputs.Select(i => KingVertexLayout.Attributes.First(x => x.Location == i.Location).Name).ToList(),
            ["attribute_layout"] = inputs.Select(i =>
            {
                var a = KingVertexLayout.Attributes.First(x => x.Location == i.Location);
                return new Dictionary<string, object?> { ["name"] = a.Name, ["location"] = i.Location, ["offset"] = a.Offset, ["components"] = a.Components };
            }).ToList(),
            ["programs"] = new Dictionary<string, object?> { ["gbuffer"] = selected["gbuffer"], ["zonly"] = selected["zonly"], ["material"] = selected["material"] },
            ["gbuffer_shader"] = $"{shading}_prog{selected["gbuffer"]}_extracted",
            ["zonly_shader"] = $"{shading}_prog{selected["zonly"]}_extracted",
            ["alpha_test"] = RenderInfo(material, "gsys_alpha_test_enable") == "true",
            ["material_ubo"] = materialFile,
            ["o_material_behave"] = behave,
            ["deferred_pass"] = BehaveToPass.GetValueOrDefault(behave, ""),
            ["samplers"] = samplers["gbuffer"],
            ["zonly_samplers"] = samplers["zonly"],
            ["material_shader"] = $"{shading}_prog{selected["material"]}_extracted",
            ["material_samplers"] = samplers["material"],
            ["render_state"] = RenderState(material),
        };
        return entry;
    }

    // The material's own choices with the shape's skin weight count and the pass over them; when no program has every choice, the nearest keeps the draw-time ones first, then the material's, then the o_ options.
    static int Select(ProgramLookup lookup, MaterialRecord material, int skinCount, string pass)
    {
        Dictionary<string, string> choices = [];
        foreach (NamePair choice in material.Choices) choices[choice.Key] = choice.Value;
        Dictionary<string, string> drawTime = new()
        {
            ["gsys_weight"] = skinCount.ToString(CultureInfo.InvariantCulture),
            ["gsys_assign_type"] = "gsys_assign_" + pass,
        };
        foreach (var (name, choice) in drawTime) choices[name] = choice;
        int program = lookup.Find(choices);
        return program >= 0 ? program : lookup.FindNearest(choices, name => drawTime.ContainsKey(name) ? 1000 : name.StartsWith("o_", StringComparison.Ordinal) ? 1 : 16);
    }

    void WriteProgram(string shaderDir, PreparedModel model, string shading, int program, List<string> problems)
    {
        _written.GetOrAdd((shaderDir + "|" + shading, program), _ =>
        {
            string? vertex = model.Glsl(program, ShaderStage.Vertex), fragment = model.Glsl(program, ShaderStage.Fragment);
            if (vertex is not null && fragment is not null) vertex = UnifySamplerBindings(vertex, fragment);
            foreach ((string? text, string extension) in new[] { (vertex, "vert"), (fragment, "frag") })
            {
                if (text is null) continue;
                string path = Path.Combine(shaderDir, $"{shading}_prog{program}_extracted.{extension}");
                string temp = $"{path}.{Environment.ProcessId}.{Environment.CurrentManagedThreadId}.tmp";
                File.WriteAllText(temp, text);
                File.Move(temp, path, overwrite: true);
            }

            return null;
        });
    }

    [GeneratedRegex(@"layout\s*\(\s*binding\s*=\s*(?<binding>\d+)\s*\)\s*uniform\s+(?<type>\w+)\s+(?<name>\w+)\s*;")]
    private static partial Regex SamplerDeclaration();

    // A texture is bound once for a program, and a GL uniform of one name has one binding, while the game numbers each stage's texture slots on its own: a sampler both stages
    // declare takes the fragment stage's binding in the vertex stage too.
    static string UnifySamplerBindings(string vertex, string fragment)
    {
        Dictionary<string, string> fragmentBindings = [];
        foreach (Match match in SamplerDeclaration().Matches(fragment)) fragmentBindings[match.Groups["name"].Value] = match.Groups["binding"].Value;
        return SamplerDeclaration().Replace(vertex, match => fragmentBindings.TryGetValue(match.Groups["name"].Value, out string? binding)
            ? $"layout (binding = {binding}) uniform {match.Groups["type"].Value} {match.Groups["name"].Value};"
            : match.Value);
    }

    [GeneratedRegex(@"layout\s*\(\s*location\s*=\s*(?<location>\d+)\s*\)\s*in\s+\w+\s+(?<name>\w+)\s*;")]
    private static partial Regex InputDeclaration();

    static List<(int Location, string Name)> Inputs(string vertexPath) =>
        File.Exists(vertexPath)
            ? [.. InputDeclaration().Matches(File.ReadAllText(vertexPath)).Select(m => (int.Parse(m.Groups["location"].Value, CultureInfo.InvariantCulture), m.Groups["name"].Value)).OrderBy(i => i.Item1)]
            : [];

    // Each shader sampler the program reads, joined through the material's sampler assignment to its texture.
    List<Dictionary<string, object?>> Samplers(PreparedModel model, int program, MaterialRecord material, Dictionary<string, string> textureFiles, string outDir, List<string> problems)
    {
        List<Dictionary<string, object?>> bindings = [];
        PreparedProgram prepared = model.Program(program);
        IReadOnlyList<PreparedSampler> shaderSamplers = model.Samplers();
        for (int i = 0; i < shaderSamplers.Count; i++)
        {
            int unit = prepared.Sampler(i).Fragment;
            if (unit < 0) continue;
            string key = shaderSamplers[i].Name;
            string assigned = material.SamplerAssigns.FirstOrDefault(a => a.Key == key)?.Value ?? "";
            if (assigned is "" or "<Default Value>") continue;
            int at = material.Samplers.ToList().FindIndex(s => s.Name == assigned);
            if (at < 0) continue;
            SamplerRecord sampler = material.Samplers[at];
            string texture = sampler.Texture, file = "", format = "";
            int width = 0, height = 0;
            int[]? compSelect = null;
            if (texture != "")
            {
                try
                {
                    if (_textures.Read(texture) is { } found)
                    {
                        (format, width, height, compSelect) = (found.Format, found.Width, found.Height, found.CompSelect);
                        file = $"{texture}_{width}x{height}_{format}.bin";
                        if (!textureFiles.ContainsKey(file))
                        {
                            File.WriteAllBytes(Path.Combine(outDir, file), found.Bytes);
                            textureFiles[file] = texture;
                        }
                    }
                    // A texture the pack lacks, or one in a format the uploader cannot put on the GPU (an uncompressed or 3D one), leaves the sampler without a file, as the older preparer did.
                }
                catch (Exception ex) when (ex is BfShaderFormatException or BfShaderUnsupportedException)
                {
                    problems.Add($"Texture {texture}: {ex.Message}");
                }
            }

            Dictionary<string, object?> binding = new()
            {
                ["unit"] = unit, ["key"] = key, ["assigned"] = assigned, ["texture"] = texture, ["file"] = file, ["format"] = format, ["width"] = width, ["height"] = height,
            };
            if (compSelect is not null) binding["comp_select"] = compSelect;
            binding["wrap_u"] = Wrap(sampler.WrapU);
            binding["wrap_v"] = Wrap(sampler.WrapV);
            bindings.Add(binding);
        }

        return bindings;
    }

    // The uploader repeats on Wrap, mirrors on Mirror and clamps on anything else.
    static string Wrap(byte mode) => mode switch { 0 => "Wrap", 1 => "Mirror", _ => "Clamp" };

    static string Options(MaterialRecord material)
    {
        StringBuilder text = new();
        foreach (NamePair choice in material.Choices) text.AppendLine($"{choice.Key}={OptionText(choice)}");
        return text.ToString();
    }

    // Boolean options are stored as 1 and 0; the one the loader reads by value is written the way the model file spells it.
    static string OptionText(NamePair choice) => choice.Key == "o_enable_hide_normal_pass" ? choice.Value switch { "1" => "True", "0" => "False", _ => choice.Value } : choice.Value;

    // An option the material leaves at its default is the shading model's default choice.
    static string ResolveOption(MaterialRecord material, ProgramLookup? lookup, string key)
    {
        if (material.Choices.FirstOrDefault(c => c.Key == key) is { } own) return own.Value;
        return lookup?.Options.FirstOrDefault(o => o.Name == key) is { } option && option.DefaultChoice >= 0 && option.DefaultChoice < option.Choices.Count ? option.Choices[option.DefaultChoice] : "";
    }

    static string RenderInfo(MaterialRecord material, string name) =>
        material.RenderInfos.FirstOrDefault(i => i.Name == name) is { Kind: 2 } info ? info.Strings.FirstOrDefault() ?? "" : "";

    static Dictionary<string, object?> RenderState(MaterialRecord material)
    {
        string S(string key) => RenderInfo(material, key);
        string mode = S("gsys_render_state_mode");
        bool blend = mode == "translucent" || (mode == "custom" && S("gsys_render_state_blend_mode") == "color");
        return new Dictionary<string, object?>
        {
            ["mode"] = mode,
            ["blend"] = blend,
            ["blend_mode"] = S("gsys_render_state_blend_mode"),
            ["rgb_op"] = S("gsys_color_blend_rgb_op"),
            ["rgb_src"] = S("gsys_color_blend_rgb_src_func"),
            ["rgb_dst"] = S("gsys_color_blend_rgb_dst_func"),
            ["alpha_op"] = S("gsys_color_blend_alpha_op"),
            ["alpha_src"] = S("gsys_color_blend_alpha_src_func"),
            ["alpha_dst"] = S("gsys_color_blend_alpha_dst_func"),
            ["depth_write"] = S("gsys_depth_test_write"),
            ["depth_func"] = S("gsys_depth_test_func"),
            ["display_face"] = S("gsys_render_state_display_face"),
        };
    }

    // The material block and, beside it, where each named parameter sits in it and which of them the shape's programs read.
    string WriteMaterial(string outDir, string shaderDir, MaterialRecord material, string materialName, string shading, Dictionary<string, int> selected)
    {
        string relative = $"matubo/{materialName}.gsys_material.bin";
        if (material.Block == default || material.Layout == default) return relative;
        File.WriteAllBytes(Path.Combine(outDir, "matubo", $"{materialName}.gsys_material.bin"), pack.Bytes(material.Block));
        UniformLayoutRecord layout = pack.UniformLayout(material.Layout);
        SlotUse use = SlotUseOf(shaderDir, shading, selected);
        List<Dictionary<string, object?>> uniforms = [];
        foreach (UniformOffset uniform in layout.Uniforms)
        {
            Dictionary<string, object?> item = new() { ["name"] = uniform.Name, ["offset"] = uniform.Offset };
            if (material.Params.FirstOrDefault(p => p.Name == uniform.Name) is { } param)
            {
                item["type"] = PackShaderParams.NameOf(param.Type);
                item["used"] = IsUsed(use, uniform.Offset, PackShaderParams.SizeOf(param.Type));
                if (param.Type is PackShaderParams.TexSrt or PackShaderParams.TexSrtEx
                    && param.Offset >= 0 && param.Offset + 24 <= material.ParamData.Length)
                {
                    ReadOnlySpan<byte> data = material.ParamData.Span[param.Offset..];
                    item["raw_srt"] = new object[]
                    {
                        BitConverter.ToInt32(data), BitConverter.ToSingle(data[4..]), BitConverter.ToSingle(data[8..]),
                        BitConverter.ToSingle(data[12..]), BitConverter.ToSingle(data[16..]), BitConverter.ToSingle(data[20..]),
                    };
                }
            }

            uniforms.Add(item);
        }

        Dictionary<string, object?> document = new() { ["material"] = materialName, ["block_size"] = layout.Size, ["uniforms"] = uniforms };
        File.WriteAllText(Path.Combine(outDir, "matubo", $"{materialName}.params.json"), JsonSerializer.Serialize(document, Json));
        return relative;
    }


    // Which components of which slots of the material block (fp_c11) a set of programs reads, found by one pass over their text.
    sealed record SlotUse(bool HasMatVar, Dictionary<int, (bool Bare, int Components)> Slots);

    [GeneratedRegex(@"fp_c11\.data\[(\d+)\](\.[xyzwrgba]+)?")]
    private static partial Regex MaterialSlot();

    static SlotUse SlotUseOf(string shaderDir, string shading, Dictionary<string, int> selected)
    {
        StringBuilder text = new();
        foreach (int program in selected.Values.Where(p => p >= 0).Distinct())
        {
            foreach (string extension in new[] { "vert", "frag" })
            {
                string path = Path.Combine(shaderDir, $"{shading}_prog{program}_extracted.{extension}");
                if (File.Exists(path)) text.Append(File.ReadAllText(path));
            }
        }

        string combined = text.ToString();
        Dictionary<int, (bool Bare, int Components)> slots = [];
        foreach (Match m in MaterialSlot().Matches(combined))
        {
            int slot = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            slots.TryGetValue(slot, out var seen);
            if (!m.Groups[2].Success)
            {
                slots[slot] = (true, seen.Components);
                continue;
            }

            int mask = seen.Components;
            foreach (char c in m.Groups[2].Value) mask |= c switch { 'x' or 'r' => 1, 'y' or 'g' => 2, 'z' or 'b' => 4, 'w' or 'a' => 8, _ => 0 };
            slots[slot] = (seen.Bare, mask);
        }

        return new SlotUse(combined.Contains("fp_c11.data[", StringComparison.Ordinal), slots);
    }

    // Whether any byte of the parameter is read as fp_c11.data[slot], bare (the whole vec4) or by a swizzle naming the byte's component. A parameter larger than a vec4 counts as used,
    // and so does every parameter when no program reads the block by that name at all, which would mean the convention did not hold.
    static bool IsUsed(SlotUse use, int byteOffset, int byteLength)
    {
        if (!use.HasMatVar || byteLength > 16) return true;
        for (int i = 0; i < Math.Max(1, byteLength / 4); i++)
        {
            int at = byteOffset + i * 4;
            if (use.Slots.TryGetValue(at / 16, out var seen) && (seen.Bare || (seen.Components & 1 << at % 16 / 4) != 0)) return true;
        }

        return false;
    }

    public void Dispose() => _textures.Dispose();
}
