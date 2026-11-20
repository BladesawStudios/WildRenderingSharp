using BfShaderSharp.Errors;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;
using KingRenderingSharp.Content.Animation.Clips;

namespace KingRenderingSharp.Content.Packs;

/// <summary>The animations of an asset pack that belong to a model, as the clips the animation code evaluates.</summary>
/// <remarks>
/// A curve is as the loader evaluates it: its type (the flags' curve type, 0 cubic, 0x10 linear, 0x20 baked float), the frames, and a row of keys for each frame (four cubic
/// coefficients, two linear values, or one value); a compressed cubic curve of 8 or 16-bit keys is decoded to its values and slopes and kept as cubic coefficients. A curve that is none
/// of these (an integer curve in a skeletal animation, a quaternion-key curve) is left out and reported, as it drives no bone transform. A material animation of the Switch formats holds
/// both parameters and texture patterns: its parameter infos become a material clip and its pattern infos a texture pattern clip, each only when the animation has any. A parameter's curves
/// are the item's float curves from the info's first curve and then its integer ones, and its constants become curves that hold one value. A curve's destination is its data offset inside
/// the parameter; the loader adds the parameter's own offset in the material block.
/// </remarks>
internal sealed class PackAnimations
{
    const byte TexSrt = 30, TexSrtEx = 31;

    readonly AssetPack _pack;
    readonly PackTextureFiles _textures;
    readonly Dictionary<string, byte[]> _textureBytes;
    readonly List<string> _problems;
    readonly Dictionary<string, BlobKey> _skeletal = new(StringComparer.Ordinal), _visibility = new(StringComparer.Ordinal);
    readonly Dictionary<string, MaterialAnimManifest> _materialClips = new(StringComparer.Ordinal);
    readonly Dictionary<string, TexturePatternAnimManifest> _patternClips = new(StringComparer.Ordinal);

    public PackAnimations(AssetPack pack, PackTextureFiles textures, ModelRecord model, IReadOnlyList<string> stems, Dictionary<string, byte[]> textureBytes, List<string> problems)
    {
        _pack = pack;
        _textures = textures;
        _textureBytes = textureBytes;
        _problems = problems;
        foreach (PackEntry entry in Of(PackEntryKind.SkeletalAnim, stems)) _skeletal[Suffix(entry, stems)] = entry.Key;
        foreach (PackEntry entry in Of(PackEntryKind.VisibilityAnim, stems)) _visibility[Suffix(entry, stems)] = entry.Key;

        Dictionary<string, MaterialRecord> materials = [];
        foreach (MaterialRef reference in model.Materials) materials.TryAdd(reference.Name, pack.Material(reference.Material));
        foreach (PackEntry entry in Of(PackEntryKind.MaterialAnim, stems))
        {
            MaterialAnimRecord anim = pack.Record(entry.Key, MaterialAnimRecord.Read);
            string safe = Safe(anim.Name);
            if (Parameters(anim, materials) is { } parameters) _materialClips[safe] = parameters;
            if (Patterns(anim, materials) is { } patterns) _patternClips[safe] = patterns;
        }

        Skeletal = [.. _skeletal.Keys.Order(StringComparer.OrdinalIgnoreCase)];
        Visibility = [.. _visibility.Keys.Order(StringComparer.OrdinalIgnoreCase)];
        Materials = [.. _materialClips.Keys.Order(StringComparer.OrdinalIgnoreCase)];
        TexturePatterns = [.. _patternClips.Keys.Order(StringComparer.OrdinalIgnoreCase)];
    }

    public IReadOnlyList<string> Skeletal { get; }

    public IReadOnlyList<string> Visibility { get; }

    public IReadOnlyList<string> Materials { get; }

    public IReadOnlyList<string> TexturePatterns { get; }

    public SkeletalAnimManifest SkeletalAnim(string name) => BuildSkeletal(_pack.Record(_skeletal[name], SkeletalAnimRecord.Read));

    public VisibilityAnimManifest VisibilityAnim(string name) => BuildVisibility(_pack.Record(_visibility[name], VisibilityAnimRecord.Read));

    public MaterialAnimManifest Material(string name) => _materialClips[name];

    public TexturePatternAnimManifest TexturePattern(string name) => _patternClips[name];

    IEnumerable<PackEntry> Of(PackEntryKind kind, IReadOnlyList<string> stems) =>
        _pack.Of(kind).Where(e => stems.Any(stem => e.Name.StartsWith(stem + "/", StringComparison.Ordinal)));

    static string Suffix(PackEntry entry, IReadOnlyList<string> stems)
    {
        string stem = stems.First(s => entry.Name.StartsWith(s + "/", StringComparison.Ordinal));
        return Safe(entry.Name[(stem.Length + 1)..]);
    }

    internal static string Safe(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    SkeletalAnimManifest BuildSkeletal(SkeletalAnimRecord anim)
    {
        SkeletalAnimManifest clip = new()
        {
            Name = anim.Name, FrameCount = anim.FrameCount, Loop = anim.Loop,
            RotationIsQuaternion = (anim.Flags & 0x7000) == 0,
            ScalingMode = (Assets.Manifests.SkeletonScalingMode)((anim.Flags >> 8) & 3),
        };
        foreach (BoneAnimRecord bone in anim.Bones)
        {
            float[] data = bone.Base.Length >= 10 ? bone.Base : [1, 1, 1, 0, 0, 0, 1, 0, 0, 0];
            BoneAnimManifestEntry entry = new()
            {
                BoneName = bone.Name,
                UseScale = (bone.Flags & 1u << 3) != 0, UseRotate = (bone.Flags & 1u << 4) != 0, UseTranslate = (bone.Flags & 1u << 5) != 0,
                SegmentScaleCompensate = (bone.Flags & 1u << 23) != 0,
                BaseScale = data[0..3], BaseTranslate = data[7..10], BaseRotate = data[3..7],
            };
            foreach (CurveRecord curve in bone.Curves)
            {
                if (PackCurve.Read(curve, out string? reason) is not { } read)
                {
                    _problems.Add($"{anim.Name}/{bone.Name}: {reason}");
                    continue;
                }

                entry.Curves.Add(new AnimCurveManifestEntry
                {
                    CurveType = read.Type, TargetOffset = (int)curve.DataOffset, StartFrame = curve.StartFrame, EndFrame = curve.EndFrame, Scale = read.Scale, Offset = read.Offset,
                    Delta = curve.Delta, PreWrap = (curve.Flags >> 8) & 3, PostWrap = (curve.Flags >> 12) & 3, Frames = read.Frames, Keys = [.. read.Rows],
                });
            }

            clip.BoneAnims.Add(entry);
        }

        return clip;
    }

    VisibilityAnimManifest BuildVisibility(VisibilityAnimRecord anim)
    {
        // The curve of each name: by the data offset when every offset is a distinct name index, otherwise in order.
        bool indexed = anim.Curves.Count > 0 && anim.Curves.All(c => c.DataOffset < anim.Names.Count) && anim.Curves.Select(c => c.DataOffset).Distinct().Count() == anim.Curves.Count;
        VisibilityAnimManifest clip = new() { Name = anim.Name, Kind = anim.Kind == 0 ? "Bone" : "Material", FrameCount = anim.FrameCount, Loop = anim.Loop };
        for (int i = 0; i < anim.Names.Count; i++)
        {
            int curveAt = indexed ? anim.Curves.ToList().FindIndex(c => c.DataOffset == i) : i < anim.Curves.Count ? i : -1;
            clip.Entries.Add(new VisibilityAnimEntry
            {
                Name = anim.Names[i], Base = i < anim.Base.Length && anim.Base[i] != 0,
                Curve = curveAt < 0 ? null : VisibilityCurve(anim.Curves[curveAt], $"{anim.Name}/{anim.Names[i]}"),
            });
        }

        return clip;
    }

    VisibilityAnimCurve? VisibilityCurve(CurveRecord curve, string where)
    {
        if (PackCurve.Read(curve, out string? reason) is not { } read)
        {
            _problems.Add($"{where}: {reason}");
            return null;
        }

        return new VisibilityAnimCurve { CurveType = read.Type, StartFrame = curve.StartFrame, EndFrame = curve.EndFrame, Offset = read.Offset, Frames = read.Frames, Keys = [.. read.Rows] };
    }

    MaterialAnimManifest? Parameters(MaterialAnimRecord anim, Dictionary<string, MaterialRecord> materials)
    {
        List<MaterialAnimMaterialEntry> entries = [];
        bool texSrt = false;
        foreach (MaterialAnimItemRecord item in anim.Items)
        {
            if (item.Params.Count == 0 || !materials.TryGetValue(item.Name, out MaterialRecord? material)) continue;
            List<MaterialAnimTarget> targets = [];
            foreach (ParamAnimInfoRecord info in item.Params)
            {
                string type = TypeName(material.Params.FirstOrDefault(p => p.Name == info.Name)?.Type);
                texSrt |= type is "TexSrt" or "TexSrtEx";
                for (int i = 0; i < info.FloatCurveCount + info.IntCurveCount; i++)
                {
                    int at = info.BeginCurve + i;
                    if (at >= item.Curves.Count) { _problems.Add($"{anim.Name}/{item.Name}/{info.Name}: curve {at} is not in the item"); break; }
                    if (Target(item.Curves[at], info.Name, type, $"{anim.Name}/{item.Name}/{info.Name}") is { } target) targets.Add(target);
                }

                for (int i = 0; i < info.ConstantCount && info.BeginConstant + i < item.Constants.Count; i++)
                {
                    ConstantRecord constant = item.Constants[info.BeginConstant + i];
                    targets.Add(new MaterialAnimTarget { Param = info.Name, ParamType = type, ByteOffset = (int)constant.DataOffset, ConstantBits = constant.Value, IsInt = false });
                }
            }

            if (targets.Count > 0) entries.Add(new MaterialAnimMaterialEntry { Material = item.Name, Targets = targets });
        }

        return entries.Count == 0 ? null : new MaterialAnimManifest { Name = anim.Name, Kind = texSrt ? "TexSrt" : "ShaderParam", FrameCount = anim.FrameCount, Loop = anim.Loop, Materials = entries };
    }

    MaterialAnimTarget? Target(CurveRecord curve, string param, string type, string where)
    {
        if (PackCurve.Read(curve, out string? reason) is not { } read)
        {
            _problems.Add($"{where}: {reason}");
            return null;
        }

        return new MaterialAnimTarget
        {
            Param = param, ParamType = type, ByteOffset = (int)curve.DataOffset, IsInt = read.Type >= 0x40, CurveType = read.Type,
            StartFrame = curve.StartFrame, EndFrame = curve.EndFrame, Scale = read.Scale, Offset = read.Offset, Frames = read.Frames, Keys = [.. read.Rows],
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

    TexturePatternAnimManifest? Patterns(MaterialAnimRecord anim, Dictionary<string, MaterialRecord> materials)
    {
        List<TexturePatternMaterialEntry> entries = [];
        foreach (MaterialAnimItemRecord item in anim.Items)
        {
            if (item.Patterns.Count == 0 || !materials.TryGetValue(item.Name, out MaterialRecord? material)) continue;
            List<TexturePatternSamplerEntry> samplers = [];
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
                        (frames, values, start, end) = (read.Frames, [.. read.Rows.Select(r => (int)read.Offset + (int)MathF.Round(r[0]))], curve.StartFrame, curve.EndFrame);
                    else _problems.Add($"{anim.Name}/{item.Name}/{info.Name}: {reason}");
                }
                else values = [info.BeginConstant < item.Constants.Count ? (int)item.Constants[info.BeginConstant].Value : 0];

                foreach (string key in keys)
                    samplers.Add(new TexturePatternSamplerEntry { Sampler = key, StartFrame = start, EndFrame = end, Frames = frames, Values = values });
            }

            if (samplers.Count > 0) entries.Add(new TexturePatternMaterialEntry { Material = item.Name, Samplers = samplers });
        }

        if (entries.Count == 0) return null;
        return new TexturePatternAnimManifest
        {
            Name = anim.Name, FrameCount = anim.FrameCount, Loop = anim.Loop, Materials = entries,
            Textures = [.. anim.TextureNames.Select(TextureEntry)],
        };
    }

    TexturePatternTextureEntry TextureEntry(string texture)
    {
        TexturePatternTextureEntry entry = new() { Texture = texture };
        try
        {
            if (_textures.Read(texture) is { } found)
            {
                string file = $"{texture}_{found.Width}x{found.Height}_{found.Format}.bin";
                lock (_textureBytes) _textureBytes.TryAdd(file, found.Bytes);
                (entry.File, entry.Format, entry.Width, entry.Height, entry.CompSelect) = (file, found.Format, found.Width, found.Height, found.CompSelect);
            }
        }
        catch (Exception ex) when (ex is BfShaderFormatException or BfShaderUnsupportedException)
        {
            _problems.Add($"Texture {texture}: {ex.Message}");
        }

        return entry;
    }
}
