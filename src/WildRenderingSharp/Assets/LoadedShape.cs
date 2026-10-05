using Silk.NET.OpenGL;

namespace WildRenderingSharp.Assets;

/// <summary>
/// One shape ready to draw: GL objects built from a <see cref="ShapeManifestEntry"/> by
/// <see cref="ModelLoader"/>. A shape always has a G-buffer variant (shapes without one are
/// dropped at load time, matching <c>load_shapes</c>'s "no gbuffer program resolved" skip);
/// the z-only and forward variants are optional per-shape, per-material.
/// </summary>
public sealed class LoadedShape
{
    public required string Name { get; init; }
    public required string Material { get; init; }
    public required string DeferredPass { get; init; }
    public required bool AlphaTest { get; init; }
    public required bool Blend { get; init; }
    public required RenderState RenderState { get; init; }

    /// <summary>
    /// <c>ShapeManifestEntry.VertexSkinCount</c>: 0 = the bone pose is baked into the exported
    /// positions, &gt;= 1 = the GPU skins this shape from the bone palette every frame. Passes that
    /// draw the geometry with their own shader (<see cref="Pipeline.PassIdMaskPass"/>) need it to
    /// pick the same transform path the real vertex shader compiled in.
    /// </summary>
    public required int VertexSkinCount { get; init; }

    public required uint VertexBuffer { get; init; }
    public required uint IndexBuffer { get; init; }
    public required int IndexCount { get; init; }

    public required uint GBufferProgram { get; init; }
    public required uint GBufferVao { get; init; }
    public required IReadOnlyList<ShapeSampler> GBufferSamplers { get; init; }

    public uint ZOnlyProgram { get; init; }
    public uint ZOnlyVao { get; init; }
    public IReadOnlyList<ShapeSampler> ZOnlySamplers { get; init; } = [];

    public uint ForwardProgram { get; init; }
    public uint ForwardVao { get; init; }
    public IReadOnlyList<ShapeSampler> ForwardSamplers { get; init; } = [];

    /// <summary>Base name of the compiled forward program's <c>.vert</c>/<c>.frag</c> pair (e.g. <c>"material_prog3248"</c>) - empty when <see cref="HasForward"/> is false. Lets <c>Debug.ShaderStepDebugger</c> find and re-instrument the real source text for this exact shape.</summary>
    public string ForwardShaderName { get; init; } = "";

    /// <summary>Base name of the compiled G-buffer program's <c>.vert</c>/<c>.frag</c> pair - lets <c>Debug.ShaderStepDebugger</c> find and re-instrument the real source text for this exact shape. Unlike <see cref="ForwardShaderName"/> this is never empty (every loaded shape has a G-buffer program).</summary>
    public string GBufferShaderName { get; init; } = "";

    /// <summary>
    /// Set by the Scene panel's shader step debugger while enabled: an instrumented variant of
    /// <see cref="ForwardProgram"/> (built by <see cref="Debug.ShaderStepDebugger"/>) that
    /// <see cref="Pipeline.ForwardPass"/> draws with INSTEAD, when non-null. Null (the default)
    /// means "draw normally." The instrumented program uses the same vertex layout/attribute
    /// locations as the real one, so <see cref="ForwardVao"/> is reused unchanged.
    /// </summary>
    public uint? DebugForwardProgram { get; set; }

    /// <summary>Which <c>temp_N</c> to visualise as the final colour when <see cref="DebugForwardProgram"/> is set; -1 shows the real, unmodified output (every debug hook in the instrumented program is a no-op at -1, since no real temp is ever numbered negative).</summary>
    public int DebugStepTarget { get; set; } = -1;

    /// <summary>
    /// Set by the Scene panel's shader step debugger while enabled and targeting the G-BUFFER
    /// program instead of forward: an instrumented variant of <see cref="GBufferProgram"/> that
    /// <see cref="Pipeline.GBufferPass"/> draws with, as an EXTRA pass over this one shape after the
    /// real G-buffer draw, with depth testing forced to always-pass. Forcing depth is what makes
    /// this able to answer "why does this shape's own alpha-test discard almost always fire" at
    /// all - the normal G-buffer draw only shows fragments the Z-only prepass already let through,
    /// so a value computed right before a discard that DOES fire is invisible through the ordinary
    /// path no matter what depth state you leave alone. Null (the default) means "draw normally."
    /// </summary>
    public uint? DebugGBufferProgram { get; set; }

    /// <summary>Which <c>temp_N</c> to visualise when <see cref="DebugGBufferProgram"/> is set; -1 shows the real output. Shares the same numbering space as <see cref="DebugStepTarget"/> conceptually but is a separate value since forward and G-buffer debugging can't both be meaningfully active for the same pixel.</summary>
    public int DebugGBufferStepTarget { get; set; } = -1;

    /// <summary>
    /// Sampler key -> the texture to bind INSTEAD of whichever this shape's own sampler list
    /// resolved for that key. Null (the common case) means "draw exactly what the material says".
    /// Set per frame by <see cref="WildRenderingSharp.Rendering.TexturePatternPose"/> and read by
    /// <see cref="Pipeline.ShapeDrawing"/>; every variant (G-buffer, z-only, forward) is covered
    /// because the substitution is by KEY, not by unit - the same sampler can sit on a different
    /// unit in each variant's own compiled program.
    /// </summary>
    public IReadOnlyDictionary<string, LoadedTexture>? SamplerOverrides { get; set; }

    public required uint MaterialUboBuffer { get; init; }

    /// <summary>
    /// The material's <c>gsys_material</c> block exactly as its <c>.gsys_material.bin</c> declares
    /// it - the baseline a shader parameter animation is applied ON TOP of, kept so dropping an
    /// anim restores the material instead of leaving the last value it wrote.
    /// </summary>
    public required byte[] MaterialUboBytes { get; init; }

    /// <summary>Where each named shader parameter sits inside that block, or null when the cache predates the layout sidecar - in which case this material simply cannot be parameter-animated (and says so) rather than being animated at a guessed offset.</summary>
    public MaterialParamLayout? MaterialParams { get; init; }

    /// <summary>Whether <see cref="MaterialUboBuffer"/> currently holds animated values rather than <see cref="MaterialUboBytes"/> - so the restore upload happens once, not every frame.</summary>
    public bool MaterialUboIsPatched { get; set; }

    /// <summary>Position-only VAO (location 0, matching <c>PassIdMaskPass</c>'s fixed shader ABI) over the same vertex/index buffers - used to stamp this shape's resolved deferred-pass ID.</summary>
    public required uint PassIdVao { get; init; }

    public bool HasZOnly => ZOnlyVao != 0;
    public bool HasForward => ForwardVao != 0;

    /// <summary>
    /// Whether this shape draws at all - a UI-driven visibility toggle (the Material Inspector's
    /// right-click "Enabled"), not authored data. Defaults to true; every pass filters the shape
    /// list by this fresh each <see cref="WildRenderingSharp.Pipeline.DeferredPipeline.RenderFrame"/>
    /// rather than it being baked into anything computed once in
    /// <see cref="WildRenderingSharp.Pipeline.DeferredPipeline.SetModel"/>, so toggling it takes effect
    /// on the very next frame.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Per-material opt-in: run this shape's forward (<c>gsys_assign_material</c>) program even
    /// though its render state is opaque/masked, not genuinely blended. A UI-driven debug toggle
    /// (the Material Inspector's per-material checkbox), not authored data - defaults to false.
    /// Genuinely blended shapes ignore this and always run forward regardless (see
    /// <see cref="Pipeline.ForwardPass.Run"/>'s filter) since that's required for correctness, not
    /// optional. See <c>docs/forward_pass_ubo_map.md</c> for why this is manual per-material rather
    /// than an auto-detected condition: the real engine's per-object gate is dynamic gameplay
    /// state WildRenderingSharp has no equivalent of, so the user opts in the specific materials that are
    /// ALWAYS meant to run this pass (e.g. a permanently-corrupted enemy) rather than WildRenderingSharp
    /// guessing.
    /// </summary>
    public bool ForceForward { get; set; }
}
