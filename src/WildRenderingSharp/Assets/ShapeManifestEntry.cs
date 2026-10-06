using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>One entry of <c>vertex_layout</c>: an attribute's fixed byte offset/component count in <c>ExportTestBench</c>'s 192-byte interleaved vertex.</summary>
public sealed class VertexLayoutEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("location")] public int Location { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
    [JsonPropertyName("components")] public int Components { get; set; }
}

/// <summary>Which compiled program index (or -1, unresolved) each pipeline stage selected for a shape's material.</summary>
public sealed class ProgramIndices
{
    [JsonPropertyName("gbuffer")] public int GBuffer { get; set; } = -1;
    [JsonPropertyName("zonly")] public int ZOnly { get; set; } = -1;
    [JsonPropertyName("material")] public int Material { get; set; } = -1;
}

/// <summary>
/// One shape from a model's manifest - everything <c>WildRenderingSharp.Assets.ModelLoader</c> needs to
/// draw it, with no BFRES/BNSH parsing of its own (that already happened offline in
/// <c>ShaderLibrary.CompileTool</c>). See <c>ExportManifest.Run</c> for the writer.
/// </summary>
public sealed class ShapeManifestEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("material")] public string Material { get; set; } = "";
    [JsonPropertyName("shading_model")] public string ShadingModel { get; set; } = "";
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

    /// <summary>Path, relative to the data directory, of this material's resolved <c>gsys_material</c> bytes - see <see cref="WildRenderingSharp.Shaders.Profiles.Totk.Ubos.MaterialUbo"/>.</summary>
    [JsonPropertyName("material_ubo")] public string MaterialUbo { get; set; } = "";
    [JsonPropertyName("o_material_behave")] public string MaterialBehave { get; set; } = "";
    /// <summary>Which <c>SystemModel.DeferredMain</c> resolve pass this shape's material resolves through (empty if unmapped).</summary>
    [JsonPropertyName("deferred_pass")] public string DeferredPass { get; set; } = "";

    [JsonPropertyName("samplers")] public List<SamplerBinding> Samplers { get; set; } = [];
    [JsonPropertyName("zonly_samplers")] public List<SamplerBinding> ZOnlySamplers { get; set; } = [];
    [JsonPropertyName("material_shader")] public string MaterialShader { get; set; } = "";
    [JsonPropertyName("material_samplers")] public List<SamplerBinding> MaterialSamplers { get; set; } = [];

    [JsonPropertyName("render_state")] public RenderState RenderState { get; set; } = new();
}
