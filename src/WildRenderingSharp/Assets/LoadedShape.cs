
namespace WildRenderingSharp.Assets;

/// <summary>
/// One shape ready to draw: GL objects built from a <see cref="ShapeManifestEntry"/> by <see cref="ModelLoader"/>. A shape
/// always has a G-buffer variant (shapes without one are dropped at load); the z-only and forward variants are optional per shape.
/// </summary>
public sealed class LoadedShape
{
    public required string Name { get; init; }
    public required string Material { get; init; }
    public required string DeferredPass { get; init; }
    public required bool AlphaTest { get; init; }
    public required bool Blend { get; init; }
    public required RenderState RenderState { get; init; }

    /// <summary><c>ShapeManifestEntry.VertexSkinCount</c>: 0 means the bone pose is baked into the exported positions, 1 or more means the GPU skins this shape from the bone palette every frame. Passes that draw geometry with their own shader need it to pick the transform path the real vertex shader compiled in.</summary>
    public required int VertexSkinCount { get; init; }

    public required uint VertexBuffer { get; init; }
    public required uint IndexBuffer { get; init; }
    public required int IndexCount { get; init; }

    /// <summary>Every level of detail, finest first, as ranges of <see cref="IndexBuffer"/>; LOD 0 is <c>(0, IndexCount)</c>. A level past the end draws the coarsest, which is what BFRES means by a shape with a shorter chain than its neighbours.</summary>
    public IReadOnlyList<(int FirstIndex, int Count)> Lods { get; init; } = [];

    /// <summary>The index range for level <paramref name="lod"/>, clamped to the levels this shape has.</summary>
    public (int FirstIndex, int Count) Lod(int lod) =>
        Lods.Count == 0 ? (0, IndexCount) : Lods[Math.Clamp(lod, 0, Lods.Count - 1)];

    public required uint GBufferProgram { get; init; }
    public uint GBufferVao { get; internal set; }
    public required IReadOnlyList<ShapeSampler> GBufferSamplers { get; init; }

    public uint ZOnlyProgram { get; init; }
    public uint ZOnlyVao { get; internal set; }
    public IReadOnlyList<ShapeSampler> ZOnlySamplers { get; init; } = [];

    public uint ForwardProgram { get; init; }
    public uint ForwardVao { get; internal set; }
    public IReadOnlyList<ShapeSampler> ForwardSamplers { get; init; } = [];

    /// <summary>Base name of the forward program's <c>.vert</c>/<c>.frag</c> pair (e.g. <c>"material_prog3248"</c>); empty when <see cref="HasForward"/> is false. Lets <c>Debug.ShaderStepDebugger</c> re-instrument the real source.</summary>
    public string ForwardShaderName { get; init; } = "";

    /// <summary>Base name of the G-buffer program's <c>.vert</c>/<c>.frag</c> pair, for <c>Debug.ShaderStepDebugger</c>. Never empty, since every loaded shape has one.</summary>
    public string GBufferShaderName { get; init; } = "";

    /// <summary>The Z-only program's base filename, empty when it has none.</summary>
    public string ZOnlyShaderName { get; init; } = "";

    /// <summary>The same three programs patched to draw many placements at once, linked the first time this shape is drawn instanced; 0 until then, or when it has no such variant.</summary>
    public uint InstancedGBufferProgram { get; internal set; }
    public uint InstancedZOnlyProgram { get; internal set; }
    public uint InstancedForwardProgram { get; internal set; }
    internal bool InstancedProgramsLinked { get; set; }

    /// <summary>Set by the shader step debugger: an instrumented variant of <see cref="ForwardProgram"/> that <see cref="Pipeline.ForwardPass"/> draws with instead when non-null. It shares the real program's vertex layout, so <see cref="ForwardVao"/> is reused.</summary>
    public uint? DebugForwardProgram { get; set; }

    /// <summary>Which <c>temp_N</c> to show as the final colour when <see cref="DebugForwardProgram"/> is set; -1 shows the real output.</summary>
    public int DebugStepTarget { get; set; } = -1;

    /// <summary>Set by the shader step debugger when targeting the G-buffer program: an instrumented variant of <see cref="GBufferProgram"/> that <see cref="Pipeline.GBufferPass"/> draws as an extra pass over this shape with depth testing forced to always-pass. That is what lets it show values computed before an alpha-test discard, which the Z-only prepass would otherwise hide.</summary>
    public uint? DebugGBufferProgram { get; set; }

    /// <summary>Which <c>temp_N</c> to show when <see cref="DebugGBufferProgram"/> is set; -1 shows the real output.</summary>
    public int DebugGBufferStepTarget { get; set; } = -1;

    /// <summary>Sampler key to the texture bound instead of the one this shape's own list resolved for that key; null draws what the material says. Set per frame by <see cref="WildRenderingSharp.Rendering.TexturePatternPose"/>. Substitution is by key because the same sampler can sit on a different unit in each variant's program.</summary>
    public IReadOnlyDictionary<string, LoadedTexture>? SamplerOverrides { get; set; }

    public required uint MaterialBuffer { get; init; }

    /// <summary>The material block exactly as its file declares it: the baseline a parameter animation applies on top of, kept so dropping an anim restores the material.</summary>
    public required byte[] MaterialBytes { get; init; }

    /// <summary>Where each named shader parameter sits inside the material block, or null when the cache predates the layout sidecar, in which case the material cannot be parameter-animated.</summary>
    public MaterialParamLayout? MaterialParams { get; init; }

    /// <summary>Whether <see cref="MaterialBuffer"/> currently holds animated values rather than <see cref="MaterialBytes"/> - so the restore upload happens once, not every frame.</summary>
    public bool MaterialIsPatched { get; set; }

    /// <summary>Position-only VAO (location 0, matching <c>PassIdMaskPass</c>'s shader ABI) over the same buffers, used to stamp this shape's deferred pass ID.</summary>
    public uint PassIdVao { get; internal set; }

    public bool HasZOnly => ZOnlyVao != 0;

    /// <summary>Kept out of everything that draws what is seen (G-buffer, pass-ID mask, forward). True for a material the game hides from its normal pass (<c>o_enable_hide_normal_pass</c>, e.g. <c>Mt_SkyOccluder</c>, a plane that blocks the sky) and for one that names no texture (<c>Mt_ShadowModel</c>, a shadow-only stand-in).</summary>
    public bool Hidden { get; init; }

    /// <summary>Whether the shape draws into the shadow map: everything but what the game hides from its normal pass.</summary>
    public bool CastsShadow { get; init; } = true;

    /// <summary>Whether the G-buffer program samples <c>cTex_ColorBuffer</c>, the lit scene behind it, as water does. Such a shape draws after the opaque scene is lit.</summary>
    public bool ReadsSceneColor { get; init; }
    public bool HasForward => ForwardVao != 0;

    /// <summary>Whether this shape draws at all: a UI visibility toggle, not authored data. Passes filter by it fresh every frame so toggling takes effect on the next.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Per-material opt-in to run the forward (<c>gsys_assign_material</c>) program though the render state is opaque or masked; a debug toggle, not authored data. Genuinely blended shapes always run forward. See <c>docs/forward_pass_ubo_map.md</c> for why this is manual: the engine's per-object gate is gameplay state with no equivalent here.</summary>
    public bool ForceForward { get; set; }
}
