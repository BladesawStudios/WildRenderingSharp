using System.Text.Json;
using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>
/// Deserialized <c>matubo/&lt;Material&gt;.params.json</c> - where each named shader parameter lives inside that material's
/// <c>gsys_material</c> block, written by <c>ShaderLibrary.CompileTool.BuildMaterialUbo.WriteParamLayout</c>. This is the missing
/// half of a shader parameter animation. The anim addresses its target as (parameter NAME, byte offset within that parameter); only
/// this table knows where that parameter actually sits in the compiled block, because the block's layout comes from the SHADER, not
/// from the material's own packed parameter blob.
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

    /// <summary>
    /// The layout beside a material's own <c>.gsys_material.bin</c>, or null if it wasn't exported (an older cache) - callers
    /// degrade to "this material cannot be animated" rather than failing to load the model.
    /// </summary>
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
