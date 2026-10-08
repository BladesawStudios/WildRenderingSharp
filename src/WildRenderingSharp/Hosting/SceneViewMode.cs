
namespace WildRenderingSharp.Hosting;

/// <summary>What a <see cref="SceneView"/> shows.</summary>
public enum SceneViewMode
{
    Final,
    HdrPreview,
    Albedo,
    Normal,
    Shadow,
    AmbientOcclusion,
    PassId,

    /// <summary>G-buffer attachment 5, the emission the resolve adds to every pixel whose albedo flags it.</summary>
    Emission,

    /// <summary>G-buffer attachment 0, the material id.</summary>
    GBuffer0,

    GBuffer2,
    GBuffer4,

    /// <summary>The normalised linear depth the screen-space passes read.</summary>
    LinearDepth,

    /// <summary>Layer 0 of the field programs' light pre-pass (ambient).</summary>
    FieldLightLayer0,

    /// <summary>Layer 1 of the field programs' light pre-pass (added to their colour).</summary>
    FieldLightLayer1,

    /// <summary>Layer 0 of the character programs' light pre-pass.</summary>
    CharaLightLayer0,

    /// <summary>What the last deferred resolve pass wrote, before it was composited (scaled down).</summary>
    LastResolve,

    /// <summary>The HDR frame straight after the deferred resolve.</summary>
    AfterResolve,

    /// <summary>The HDR frame after the forward pass.</summary>
    AfterForward,

    /// <summary>The HDR frame after the lens flare, the last step before exposure.</summary>
    AfterFlare,

    /// <summary>What one deferred resolve pass wrote on its own (<see cref="SceneView.ResolvePassIndex"/>), shown in HDR.</summary>
    ResolvePass,
}
