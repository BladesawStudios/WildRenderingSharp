using System.Text.Json;
using System.Text.Json.Serialization;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Assets;

/// <summary>One parameter's byte layout inside a compiled <c>gsys_material</c> block.</summary>
public sealed class MaterialUniformEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("offset")] public int Offset { get; set; }

    /// <summary>
    /// The BFRES <c>ShaderParamType</c> name (e.g. "Float4", "Int", "TexSrt") the MATERIAL itself
    /// declares for this parameter, or null when the uniform sits at the shader's own default with
    /// no material override - there is then no authored type to report (the compiled block carries
    /// no component-count/type of its own on this platform). A live editor should treat null as
    /// "no known widget for this one" rather than guessing.
    /// </summary>
    [JsonPropertyName("type")] public string? Type { get; set; }

    /// <summary>
    /// Whether this shape's own real decompiled shader (G-buffer/Z-only/forward, whichever exist)
    /// actually references this parameter - null when unknown (an older cache predating this
    /// field, or <see cref="Type"/> is itself null). A live editor should treat null the same as
    /// true (show it) rather than hide anything it isn't certain about.
    /// </summary>
    [JsonPropertyName("used")] public bool? Used { get; set; }

    /// <summary>
    /// The material's AUTHORED (mode, scaleX, scaleY, rotation, translateX, translateY) for a
    /// TexSrt/TexSrtEx-typed parameter only - null for every other type. This is the pre-bake form;
    /// <see cref="MaterialAnimPose"/> needs it because an animation can drive just one sub-field
    /// (a scroll anim touching only translateY, say) and has to re-bake the whole six-value set
    /// every frame, using this as the baseline for whichever sub-fields the anim leaves untouched -
    /// see that class's own remarks and <see cref="WildRenderingSharp.Rendering.TexSrtBake"/>.
    /// </summary>
    [JsonPropertyName("raw_srt")] public float[]? RawSrt { get; set; }
}

/// <summary>
/// Deserialized <c>matubo/&lt;Material&gt;.params.json</c> - where each named shader parameter lives
/// inside that material's <c>gsys_material</c> block, written by
/// <c>ShaderLibrary.CompileTool.BuildMaterialUbo.WriteParamLayout</c>.
///
/// This is the missing half of a shader parameter animation. The anim addresses its target as
/// (parameter NAME, byte offset within that parameter); only this table knows where that parameter
/// actually sits in the compiled block, because the block's layout comes from the SHADER, not from
/// the material's own packed parameter blob.
/// </summary>
public sealed class MaterialParamLayout
{
    [JsonPropertyName("material")] public string Material { get; set; } = "";
    [JsonPropertyName("block_size")] public int BlockSize { get; set; }
    [JsonPropertyName("uniforms")] public List<MaterialUniformEntry> Uniforms { get; set; } = [];

    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    Dictionary<string, int>? _byName;
    Dictionary<string, MaterialUniformEntry>? _entryByName;

    public bool TryGetOffset(string paramName, out int offset)
    {
        _byName ??= Uniforms
            .GroupBy(u => u.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Offset, StringComparer.Ordinal);
        return _byName.TryGetValue(paramName, out offset);
    }

    /// <summary>The full entry (needed for its <see cref="MaterialUniformEntry.RawSrt"/> baseline), not just its offset.</summary>
    public bool TryGetEntry(string paramName, out MaterialUniformEntry entry)
    {
        _entryByName ??= Uniforms
            .GroupBy(u => u.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return _entryByName.TryGetValue(paramName, out entry!);
    }

    public static MaterialParamLayout Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<MaterialParamLayout>(stream, JsonOptions)
            ?? throw new InvalidDataException($"'{path}' did not deserialize to a material param layout.");
    }

    /// <summary>The layout beside a material's own <c>.gsys_material.bin</c>, or null if it wasn't exported (an older cache) - callers degrade to "this material cannot be animated" rather than failing to load the model.</summary>
    public static MaterialParamLayout? TryLoadBeside(string dataDirectory, string materialUboRelativePath)
    {
        const string suffix = ".gsys_material.bin";
        if (string.IsNullOrEmpty(materialUboRelativePath) || !materialUboRelativePath.EndsWith(suffix, StringComparison.Ordinal))
            return null;
        // Plain concatenation, not Path.ChangeExtension - the material's name is the whole stem and
        // ChangeExtension would treat ".params" as the extension it is replacing.
        string path = Path.Combine(dataDirectory, materialUboRelativePath[..^suffix.Length] + ".params.json");
        try
        {
            return File.Exists(path) ? Load(path) : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MaterialParamLayout] SKIPPED '{path}': {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// One thing a material anim writes: four bytes at <c>byte_offset</c> within the shader parameter
/// <c>param</c>, either from a curve or as a fixed value.
/// </summary>
public sealed class MaterialAnimTarget
{
    [JsonPropertyName("param")] public string Param { get; set; } = "";
    /// <summary>The BFRES <c>ShaderParamType</c> name, for display - the evaluation never needs it (a curve's own type says whether it produces int or float bits, and a constant is raw bits either way).</summary>
    [JsonPropertyName("param_type")] public string ParamType { get; set; } = "";
    /// <summary>Byte offset WITHIN the parameter (<c>AnimCurve.AnimDataOffset</c>): 0 for a plain float, 20 for a TexSrt's translate-Y, and so on.</summary>
    [JsonPropertyName("byte_offset")] public int ByteOffset { get; set; }

    /// <summary>Set on a constant target - the raw 32 bits to store, needing no float/int interpretation because the shader's own reading of that byte decides.</summary>
    [JsonPropertyName("constant_bits")] public uint? ConstantBits { get; set; }

    [JsonPropertyName("is_int")] public bool IsInt { get; set; }
    [JsonPropertyName("curve_type")] public int CurveType { get; set; }
    [JsonPropertyName("start_frame")] public float StartFrame { get; set; }
    [JsonPropertyName("end_frame")] public float EndFrame { get; set; }
    [JsonPropertyName("scale")] public float Scale { get; set; } = 1f;
    /// <summary>The curve's base value. For an int curve this is its Int32 view (exported as a whole number); <see cref="Bits"/> reads it accordingly.</summary>
    [JsonPropertyName("offset")] public float Offset { get; set; }
    [JsonPropertyName("frames")] public float[] Frames { get; set; } = [];
    [JsonPropertyName("keys")] public float[][] Keys { get; set; } = [];

    /// <summary>The 32 bits this target writes at <paramref name="frame"/> - a constant's stored bits, or the curve's evaluated float/int reinterpreted as bits.</summary>
    public uint Bits(float frame)
    {
        if (ConstantBits is { } bits)
            return bits;
        if (IsInt)
            return unchecked((uint)AnimCurveEval.EvaluateInt(StartFrame, EndFrame, (int)Offset, Frames, Keys, frame));
        return BitConverter.SingleToUInt32Bits(
            AnimCurveEval.EvaluateFloat(CurveType, StartFrame, EndFrame, Scale, Offset, Frames, Keys, frame));
    }
}

/// <summary>Every target of one material.</summary>
public sealed class MaterialAnimMaterialEntry
{
    [JsonPropertyName("material")] public string Material { get; set; } = "";
    [JsonPropertyName("targets")] public List<MaterialAnimTarget> Targets { get; set; } = [];
}

/// <summary>
/// Deserialized <c>&lt;Model&gt;.&lt;AnimName&gt;.matanim.json</c> - a shader PARAMETER animation,
/// which is the single mechanism behind BFRES's <c>_fsp</c> (shader param), <c>_fcl</c> (colour)
/// and <c>_fts</c> (texture SRT) anims alike. Every one of them writes 4-byte words into the
/// material's <c>gsys_material</c> block; see <c>ShaderLibrary.CompileTool.ExportMaterialAnim</c>
/// for the derivation and <see cref="WildRenderingSharp.Rendering.MaterialAnimPose"/> for the runtime.
/// </summary>
public sealed class MaterialAnimManifest : IAnimClip
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>"TexSrt" when every parameter this anim drives is an SRT type, else the BFRES bucket ("ShaderParam"/"Color"). Classified by what it actually touches, because BfresLibrary's own bucketing files <c>_fts</c> anims under ShaderParam.</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("frame_count")] public int FrameCount { get; set; }
    [JsonPropertyName("loop")] public bool Loop { get; set; }
    [JsonPropertyName("materials")] public List<MaterialAnimMaterialEntry> Materials { get; set; } = [];

    public bool IsTextureSrt => string.Equals(Kind, "TexSrt", StringComparison.Ordinal);

    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static MaterialAnimManifest Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<MaterialAnimManifest>(stream, JsonOptions)
            ?? throw new InvalidDataException($"'{path}' did not deserialize to a material anim manifest.");
    }

    /// <summary>Every material anim's name next to a model's manifest (<c>&lt;modelName&gt;.&lt;AnimName&gt;.matanim.json</c>).</summary>
    public static IEnumerable<string> ListAvailable(string dataDirectory, string modelName) =>
        Directory.Exists(dataDirectory)
            ? Directory.EnumerateFiles(dataDirectory, $"{modelName}.*.matanim.json")
                .Select(Path.GetFileName)
                .Select(n => n![(modelName.Length + 1)..^".matanim.json".Length])
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            : [];
}
