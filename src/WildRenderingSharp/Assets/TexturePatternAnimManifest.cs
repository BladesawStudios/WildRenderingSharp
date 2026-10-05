using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>One texture a pattern anim can select, with everything <see cref="TextureCache"/> needs to load it - the same shape as a <see cref="SamplerBinding"/> minus the unit/key, which belong to the sampler entry that selects it rather than to the texture.</summary>
public sealed class TexturePatternTextureEntry
{
    [JsonPropertyName("texture")] public string Texture { get; set; } = "";
    /// <summary>File name relative to the data directory, or empty if the texture failed to export - the entry is still listed so it can be logged rather than silently dropped.</summary>
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("format")] public string Format { get; set; } = "";
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
}

/// <summary>
/// One sampler of one material, and the step curve of texture indices driving it.
///
/// <see cref="Frames"/> and <see cref="Values"/> are parallel; an entry with no curve at all (a
/// constant selection for the whole anim) exports an empty <see cref="Frames"/> and a single
/// <see cref="Values"/> element, so <see cref="Evaluate"/> needs no separate case for it.
/// </summary>
public sealed class TexturePatternSamplerEntry
{
    /// <summary>The sampler this drives - matches <see cref="SamplerBinding.Key"/> (e.g. "_e0"), which is what <c>PatternAnimInfo.Name</c> holds.</summary>
    [JsonPropertyName("sampler")] public string Sampler { get; set; } = "";
    [JsonPropertyName("start_frame")] public float StartFrame { get; set; }
    [JsonPropertyName("end_frame")] public float EndFrame { get; set; }
    [JsonPropertyName("frames")] public float[] Frames { get; set; } = [];
    /// <summary>Index into the anim's own <see cref="TexturePatternAnimManifest.Textures"/>, already including the curve's integer <c>Offset</c>.</summary>
    [JsonPropertyName("values")] public int[] Values { get; set; } = [];

    /// <summary>
    /// The texture index at <paramref name="frame"/>. STEP, not interpolated: the value of the
    /// last key at or before the frame is held until the next key. That is exactly what the
    /// shipped evaluator does - <c>ResAnimCurve::EvaluateInt</c> (Ghidra 0x71009b774c) dispatches
    /// to the step function at 0x7100073a68, which returns <c>keys[FindFrame(frame)]</c> with no
    /// blend of any kind, and adds the curve's integer <c>Offset</c> (already folded into
    /// <see cref="Values"/> at export).
    /// </summary>
    public int Evaluate(float frame)
    {
        if (Values.Length == 0)
            return -1;
        if (Frames.Length == 0)
            return Values[0];

        float f = Math.Clamp(frame, StartFrame, EndFrame);
        int i = 0;
        while (i + 1 < Frames.Length && Frames[i + 1] <= f)
            i++;
        return Values[Math.Min(i, Values.Length - 1)];
    }
}

/// <summary>Every sampler of one material that this anim drives.</summary>
public sealed class TexturePatternMaterialEntry
{
    /// <summary>The material name, matched against <see cref="LoadedShape.Material"/> - one material can back several shapes, and the swap applies to all of them.</summary>
    [JsonPropertyName("material")] public string Material { get; set; } = "";
    [JsonPropertyName("samplers")] public List<TexturePatternSamplerEntry> Samplers { get; set; } = [];
}

/// <summary>
/// Deserialized <c>&lt;Model&gt;.&lt;AnimName&gt;.texpat.json</c>, written by
/// <c>ShaderLibrary.CompileTool.ExportTexturePatternAnim</c> from a BFRES texture pattern anim
/// (an FMAA with <c>TexturePatternCount &gt; 0</c>).
///
/// A texture pattern anim does not move or shade anything - it re-points a material's SAMPLER at a
/// different texture per frame. See <c>ExportTexturePatternAnim</c> for the Ghidra citations behind
/// that; <see cref="WildRenderingSharp.Rendering.TexturePatternPose"/> is the runtime that applies it.
/// </summary>
public sealed class TexturePatternAnimManifest : IAnimClip
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("frame_count")] public int FrameCount { get; set; }
    [JsonPropertyName("loop")] public bool Loop { get; set; }
    /// <summary>The anim's own texture list - <see cref="TexturePatternSamplerEntry.Values"/> indexes into this.</summary>
    [JsonPropertyName("textures")] public List<TexturePatternTextureEntry> Textures { get; set; } = [];
    [JsonPropertyName("materials")] public List<TexturePatternMaterialEntry> Materials { get; set; } = [];

    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static TexturePatternAnimManifest Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<TexturePatternAnimManifest>(stream, JsonOptions)
            ?? throw new InvalidDataException($"'{path}' did not deserialize to a texture pattern anim manifest.");
    }

    /// <summary>Every texture pattern anim's name next to a model's manifest (<c>&lt;modelName&gt;.&lt;AnimName&gt;.texpat.json</c>).</summary>
    public static IEnumerable<string> ListAvailable(string dataDirectory, string modelName) =>
        Directory.Exists(dataDirectory)
            ? Directory.EnumerateFiles(dataDirectory, $"{modelName}.*.texpat.json")
                .Select(Path.GetFileName)
                .Select(n => n![(modelName.Length + 1)..^".texpat.json".Length])
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            : [];

    /// <summary>The <see cref="SamplerBinding"/> for one of this anim's textures as seen through one sampler - the shape <see cref="TextureCache"/> loads from. <paramref name="samplerKey"/> matters because the sRGB decision reads it.</summary>
    public SamplerBinding? BindingFor(int textureIndex, string samplerKey)
    {
        if (textureIndex < 0 || textureIndex >= Textures.Count)
            return null;
        var t = Textures[textureIndex];
        if (string.IsNullOrEmpty(t.File))
            return null;
        return new SamplerBinding
        {
            Unit = -1, // the unit belongs to the shape's own sampler list, not to the anim
            Key = samplerKey,
            Assigned = samplerKey,
            Texture = t.Texture,
            File = t.File,
            Format = t.Format,
            Width = t.Width,
            Height = t.Height,
        };
    }
}
