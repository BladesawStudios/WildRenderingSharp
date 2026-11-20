using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Errors;
using BfShaderSharp.Lookup;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;
using BfShaderSharp.Prepared;
using KingRenderingSharp.Content.Animation.Clips;
using KingRenderingSharp.Content.Assets.Manifests;
using KingRenderingSharp.Content.Assets.Materials;
using KingRenderingSharp.Content.Assets.Sources;

namespace KingRenderingSharp.Content.Packs;

/// <summary>A model of an asset pack, read as the renderer draws it: the manifest and skeleton worked out from the pack's records, and the vertex, index, material and texture bytes made when a shape asks.</summary>
/// <remarks>
/// A program is chosen for each pass from the material's own option choices and the draw-time options of the shape: its skin weight count and the pass. The programs themselves are the
/// shaders store's (<see cref="PackPrograms"/>), named in the manifest as <c>&lt;shading&gt;_prog&lt;n&gt;_extracted</c>. A program that is not in the prepared archives is reported in
/// <see cref="Problems"/> and left unresolved, which leaves its shape undrawable.
/// </remarks>
internal sealed partial class PackModelSource : IModelSource
{
    static readonly string[] Passes = ["gbuffer", "zonly", "material"];

    // o_material_behave to the deferred pass whose materials carry that value; 1 is shared by chara_metal, clear and debug_simple and 2 by the field family.
    static readonly Dictionary<string, string> BehaveToPass = new()
    {
        ["0"] = "chara_nonmetal", ["1"] = "chara_metal", ["10"] = "chara_grossy",
        ["100"] = "chara_hair", ["101"] = "chara_skin", ["104"] = "chara_eye",
        ["105"] = "field_leaf", ["103"] = "field_water", ["2"] = "field_hybrid", ["102"] = "field_miasma",
    };

    readonly AssetPack _pack;
    readonly PackPrograms _programs;
    readonly PackTextureFiles _textures;
    readonly Dictionary<string, Func<byte[]>> _files = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);
    readonly Dictionary<string, Func<MaterialParamLayout?>> _layouts = new(StringComparer.Ordinal);
    readonly Dictionary<string, byte[]> _textureBytes = new(StringComparer.Ordinal);
    readonly PackAnimations _animations;

    public PackModelSource(AssetPack pack, PackPrograms programs, PackTextureFiles textures, string packModelName, string modelName, IReadOnlyList<string> animationStems, bool clampEyes = true)
    {
        _pack = pack;
        _programs = programs;
        _textures = textures;
        Name = modelName;
        ModelRecord model = pack.Model(packModelName) ?? throw new KeyNotFoundException($"The pack has no model {packModelName}");
        SkeletonRecord skeleton = pack.Skeleton(model.Skeleton);
        Skeleton = PackSkeleton.ToManifest(skeleton);
        Matrix4x4[] world = PackSkeleton.BoneWorldMatrices(skeleton);

        ModelManifest manifest = new()
        {
            Model = modelName,
            VertexStride = KingVertexLayout.Stride,
            VertexLayout = [.. KingVertexLayout.Attributes.Select(a => new VertexLayoutEntry { Name = a.Name, Location = a.Location, Offset = a.Offset, Components = a.Components })],
        };
        foreach (ShapeRef reference in model.Shapes)
        {
            ShapeRecord shape = pack.Shape(reference.Shape);
            MaterialRef materialRef = model.Materials[reference.MaterialIndex];
            MaterialRecord material = pack.Material(materialRef.Material);
            manifest.Shapes.Add(ShapeEntry(reference, shape, material, materialRef.Name, reference.Name.Replace(':', '_').Replace('/', '_'), world));
        }

        if (clampEyes) manifest.ClampEyes();
        Manifest = manifest;
        _animations = new PackAnimations(pack, textures, model, animationStems, _textureBytes, Problems);
    }

    public string Name { get; }

    public ModelManifest Manifest { get; }

    public SkeletonManifest? Skeleton { get; }

    /// <summary>What could not be carried over, one line each: a program the archives lack, a texture in a format the renderer cannot upload.</summary>
    public List<string> Problems { get; } = [];

    /// <summary>The names of the files the manifest may name; the texture files are those the shapes and animations have asked for so far.</summary>
    public IEnumerable<string> FileNames => _files.Keys.Concat(_textureBytes.Keys);

    public bool Has(string file) => _files.ContainsKey(file) || _textureBytes.ContainsKey(file);

    public byte[] Bytes(string file) =>
        _files.TryGetValue(file, out Func<byte[]>? make) ? make()
        : _textureBytes.TryGetValue(file, out byte[]? bytes) ? bytes
        : throw new FileNotFoundException($"Model {Name} has no file {file}", file);

    public string? Options(string shapeName) => _options.GetValueOrDefault(shapeName);

    public MaterialParamLayout? ParamLayout(string materialFile) => _layouts.TryGetValue(materialFile, out Func<MaterialParamLayout?>? make) ? make() : null;

    public IReadOnlyList<string> SkeletalAnims => _animations.Skeletal;

    public IReadOnlyList<string> TexturePatternAnims => _animations.TexturePatterns;

    public IReadOnlyList<string> MaterialAnims => _animations.Materials;

    public IReadOnlyList<string> VisibilityAnims => _animations.Visibility;

    public SkeletalAnimManifest SkeletalAnim(string name) => _animations.SkeletalAnim(name);

    public TexturePatternAnimManifest TexturePatternAnim(string name) => _animations.TexturePattern(name);

    public MaterialAnimManifest MaterialAnim(string name) => _animations.Material(name);

    public VisibilityAnimManifest VisibilityAnim(string name) => _animations.VisibilityAnim(name);

    ShapeManifestEntry ShapeEntry(ShapeRef reference, ShapeRecord shape, MaterialRecord material, string materialName, string safe, Matrix4x4[] world)
    {
        Matrix4x4 rigid = shape.SkinCount == 0 && reference.BoneIndex < world.Length ? world[reference.BoneIndex] : Matrix4x4.Identity;
        string vertexFile = $"{safe}_vtx.bin", indexFile = $"{safe}_idx.bin";
        _files[vertexFile] = () => KingVertexPacker.Vertices(shape, rigid, _pack.Bytes);
        (byte[] indices, List<int[]> lods) = KingVertexPacker.Indices(shape, _pack.Bytes);
        _files[indexFile] = () => indices;
        _options[reference.Name] = Options(material);

        Dictionary<string, int> selected = [];
        Dictionary<string, List<SamplerBinding>> samplers = [];
        PreparedModel? preparedModel = null;
        ProgramLookup? lookup = null;
        string shading = material.ShadingModel;
        if (material.Program >= 0 || material.ShaderArchive != "")
        {
            preparedModel = _programs.Model(shading);
            if (preparedModel is not null) lookup = _lookups.GetOrAdd(material.ShaderArchive + "/" + shading, _ => preparedModel.CreateLookup());
            else Problems.Add($"{materialName}: no shader file holds the shading model {shading}");
        }

        foreach (string pass in Passes)
        {
            int program = preparedModel is not null && lookup is not null ? Select(lookup, material, shape.SkinCount, pass) : -1;
            if (program >= 0 && _programs.Text(shading, program) is (null, null))
            {
                Problems.Add($"{reference.Name}: program {program} of {shading} for {pass} was not prepared");
                program = -1;
            }

            selected[pass] = program;
            samplers[pass] = program >= 0 ? Samplers(preparedModel!, program, material) : [];
        }

        List<(int Location, string Name)> inputs = selected["gbuffer"] >= 0 ? Inputs(_programs.Text(shading, selected["gbuffer"]).Vertex) : [];
        inputs = [.. inputs.Where(i => KingVertexLayout.Attributes.Any(a => a.Location == i.Location))];
        string behave = ResolveOption(material, lookup, "o_material_behave");
        string materialFile = RegisterMaterial(material, materialName, shading, selected);

        return new ShapeManifestEntry
        {
            Name = reference.Name,
            Material = materialName,
            VertexFile = vertexFile,
            IndexFile = indexFile,
            IndexCount = lods[0][1],
            Lods = lods,
            VertexSkinCount = (int)shape.SkinCount,
            BoneIndex = (int)reference.BoneIndex,
            VertexFormat = KingVertexLayout.Format(inputs.Select(i => i.Location).ToHashSet()),
            Attributes = [.. inputs.Select(i => KingVertexLayout.Attributes.First(x => x.Location == i.Location).Name)],
            AttributeLayout = [.. inputs.Select(i =>
            {
                var a = KingVertexLayout.Attributes.First(x => x.Location == i.Location);
                return new VertexLayoutEntry { Name = a.Name, Location = i.Location, Offset = a.Offset, Components = a.Components };
            })],
            Programs = new ProgramIndices { GBuffer = selected["gbuffer"], ZOnly = selected["zonly"], Material = selected["material"] },
            GBufferShader = $"{shading}_prog{selected["gbuffer"]}_extracted",
            ZOnlyShader = $"{shading}_prog{selected["zonly"]}_extracted",
            AlphaTest = RenderInfo(material, "gsys_alpha_test_enable") == "true",
            MaterialFile = materialFile,
            Samplers = samplers["gbuffer"],
            ZOnlySamplers = samplers["zonly"],
            MaterialShader = $"{shading}_prog{selected["material"]}_extracted",
            MaterialSamplers = samplers["material"],
            RenderState = RenderStateOf(material),
            ShadingModel = shading,
            DeferredPass = BehaveToPass.GetValueOrDefault(behave, ""),
            MaterialBehave = behave,
        };
    }

    // Per source: a lookup reads the memory-mapped archive its programs came from, which is unmapped when the pack is disposed, so one cached beyond the pack would read freed memory.
    readonly ConcurrentDictionary<string, ProgramLookup> _lookups = new();

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

    [GeneratedRegex(@"layout\s*\(\s*location\s*=\s*(?<location>\d+)\s*\)\s*in\s+\w+\s+(?<name>\w+)\s*;")]
    private static partial Regex InputDeclaration();

    static List<(int Location, string Name)> Inputs(string? vertexText) =>
        vertexText is null ? [] : [.. InputDeclaration().Matches(vertexText).Select(m => (int.Parse(m.Groups["location"].Value, CultureInfo.InvariantCulture), m.Groups["name"].Value)).OrderBy(i => i.Item1)];

    // Each shader sampler the program reads, joined through the material's sampler assignment to its texture.
    List<SamplerBinding> Samplers(PreparedModel model, int program, MaterialRecord material)
    {
        List<SamplerBinding> bindings = [];
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
                        lock (_textureBytes) _textureBytes.TryAdd(file, found.Bytes);
                    }
                    // A texture the pack lacks, or one in a format the uploader cannot put on the GPU (an uncompressed or 3D one), leaves the sampler without a file.
                }
                catch (Exception ex) when (ex is BfShaderFormatException or BfShaderUnsupportedException)
                {
                    Problems.Add($"Texture {texture}: {ex.Message}");
                }
            }

            bindings.Add(new SamplerBinding
            {
                Unit = unit, Key = key, Assigned = assigned, Texture = texture, File = file, Format = format, Width = width, Height = height,
                CompSelect = compSelect, WrapU = Wrap(sampler.WrapU), WrapV = Wrap(sampler.WrapV),
            });
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

    static RenderState RenderStateOf(MaterialRecord material)
    {
        string S(string key) => RenderInfo(material, key);
        string mode = S("gsys_render_state_mode");
        bool blend = mode == "translucent" || (mode == "custom" && S("gsys_render_state_blend_mode") == "color");
        return new RenderState
        {
            Mode = mode, Blend = blend, BlendMode = S("gsys_render_state_blend_mode"),
            RgbOp = S("gsys_color_blend_rgb_op"), RgbSrc = S("gsys_color_blend_rgb_src_func"), RgbDst = S("gsys_color_blend_rgb_dst_func"),
            AlphaOp = S("gsys_color_blend_alpha_op"), AlphaSrc = S("gsys_color_blend_alpha_src_func"), AlphaDst = S("gsys_color_blend_alpha_dst_func"),
            DepthWrite = S("gsys_depth_test_write"), DepthFunc = S("gsys_depth_test_func"), DisplayFace = S("gsys_render_state_display_face"),
        };
    }

    // The material block and, beside it, where each named parameter sits in it and which of the shape's programs read them.
    string RegisterMaterial(MaterialRecord material, string materialName, string shading, Dictionary<string, int> selected)
    {
        string relative = $"matubo/{materialName}.gsys_material.bin";
        if (material.Block == default || material.Layout == default) return relative;
        _files[relative] = () => _pack.Bytes(material.Block);
        Dictionary<string, int> programs = new(selected);
        _layouts[relative] = () => ParamLayoutOf(material, materialName, shading, programs);
        return relative;
    }

    MaterialParamLayout ParamLayoutOf(MaterialRecord material, string materialName, string shading, Dictionary<string, int> selected)
    {
        UniformLayoutRecord layout = _pack.UniformLayout(material.Layout);
        SlotUse use = SlotUseOf(shading, selected);
        MaterialParamLayout result = new() { Material = materialName, BlockSize = layout.Size };
        foreach (UniformOffset uniform in layout.Uniforms)
        {
            MaterialUniformEntry item = new() { Name = uniform.Name, Offset = uniform.Offset };
            if (material.Params.FirstOrDefault(p => p.Name == uniform.Name) is { } param)
            {
                item.Type = PackShaderParams.NameOf(param.Type);
                item.Used = IsUsed(use, uniform.Offset, PackShaderParams.SizeOf(param.Type));
                if (param.Type is PackShaderParams.TexSrt or PackShaderParams.TexSrtEx
                    && param.Offset >= 0 && param.Offset + 24 <= material.ParamData.Length)
                {
                    ReadOnlySpan<byte> data = material.ParamData.Span[param.Offset..];
                    item.RawSrt =
                    [
                        (float)BitConverter.ToInt32(data), BitConverter.ToSingle(data[4..]), BitConverter.ToSingle(data[8..]),
                        BitConverter.ToSingle(data[12..]), BitConverter.ToSingle(data[16..]), BitConverter.ToSingle(data[20..]),
                    ];
                }
            }

            result.Uniforms.Add(item);
        }

        return result;
    }

    // Which components of which slots of the material block (fp_c11) a set of programs reads, found by one pass over their text.
    sealed record SlotUse(bool HasMatVar, Dictionary<int, (bool Bare, int Components)> Slots);

    [GeneratedRegex(@"fp_c11\.data\[(\d+)\](\.[xyzwrgba]+)?")]
    private static partial Regex MaterialSlot();

    SlotUse SlotUseOf(string shading, Dictionary<string, int> selected)
    {
        StringBuilder text = new();
        foreach (int program in selected.Values.Where(p => p >= 0).Distinct())
        {
            (string? vertex, string? fragment) = _programs.Text(shading, program);
            if (vertex is not null) text.Append(vertex);
            if (fragment is not null) text.Append(fragment);
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
}
