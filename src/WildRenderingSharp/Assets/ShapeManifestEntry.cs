using System.Text.Json;
using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>
/// One shape from a model's manifest - everything <c>WildRenderingSharp.Assets.ModelLoader</c> needs to
/// draw it, with no BFRES/BNSH parsing of its own (that already happened offline in
/// <c>ShaderLibrary.CompileTool</c>). See <c>ExportManifest.Run</c> for the writer.
/// </summary>
public sealed class ShapeManifestEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("material")] public string Material { get; set; } = "";
    [JsonPropertyName("vertex_file")] public string VertexFile { get; set; } = "";
    [JsonPropertyName("index_file")] public string IndexFile { get; set; } = "";
    [JsonPropertyName("index_count")] public int IndexCount { get; set; }

    /// <summary>
    /// Every level of detail in <see cref="IndexFile"/>, finest first, as <c>[first index, count]</c>.
    /// LOD 0 is always <c>[0, IndexCount]</c>, and the coarser ones follow it in the same file. Null
    /// for a model prepared before levels of detail were exported - it has only LOD 0.
    /// </summary>
    [JsonPropertyName("lods")] public List<int[]>? Lods { get; set; }

    /// <summary>0 = rigid (bone space, <see cref="BoneIndex"/>), 1 = single-bind (bone space, per-vertex), &gt;=2 = smooth (already model space). <c>ExportTestBench</c> bakes 0 and 1 into the exported positions.</summary>
    [JsonPropertyName("vertex_skin_count")] public int VertexSkinCount { get; set; }
    [JsonPropertyName("bone_index")] public int BoneIndex { get; set; }

    [JsonPropertyName("vertex_format")] public string VertexFormat { get; set; } = "";
    [JsonPropertyName("attributes")] public List<string> Attributes { get; set; } = [];
    [JsonPropertyName("attribute_layout")] public List<VertexLayoutEntry> AttributeLayout { get; set; } = [];

    [JsonPropertyName("programs")] public ProgramIndices Programs { get; set; } = new();
    [JsonPropertyName("gbuffer_shader")] public string GBufferShader { get; set; } = "";
    [JsonPropertyName("zonly_shader")] public string ZOnlyShader { get; set; } = "";
    [JsonPropertyName("alpha_test")] public bool AlphaTest { get; set; }

    /// <summary>Path, relative to the data directory, of this material's resolved material block bytes.</summary>
    [JsonPropertyName("material_ubo")] public string MaterialFile { get; set; } = "";

    [JsonPropertyName("samplers")] public List<SamplerBinding> Samplers { get; set; } = [];
    [JsonPropertyName("zonly_samplers")] public List<SamplerBinding> ZOnlySamplers { get; set; } = [];
    [JsonPropertyName("material_shader")] public string MaterialShader { get; set; } = "";
    [JsonPropertyName("material_samplers")] public List<SamplerBinding> MaterialSamplers { get; set; } = [];

    [JsonPropertyName("render_state")] public RenderState RenderState { get; set; } = new();

    /// <summary>Keys the loader does not know, kept for the game profile that wrote them.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement> Extensions { get; set; } = [];
}
