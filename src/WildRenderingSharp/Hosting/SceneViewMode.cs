
namespace WildRenderingSharp.Hosting;

/// <summary>What a <see cref="SceneView"/> shows.</summary>
public enum SceneViewMode
{
    /// <summary>The graded, tonemapped frame.</summary>
    Final,
    /// <summary>The raw HDR buffer before exposure, Reinhard-mapped - for judging what the shaders output.</summary>
    HdrPreview,
    /// <summary>G-buffer albedo.</summary>
    Albedo,
    /// <summary>G-buffer normal (packed .xy only - "is the G-buffer populated at all").</summary>
    Normal,
    /// <summary><c>cTex_PreShadow</c>, the renderer's synthesised sun visibility.</summary>
    Shadow,
    /// <summary><c>cTex_PreMisc</c>, the renderer's synthesised screen-space AO / N.L.</summary>
    AmbientOcclusion,
    /// <summary>The deferred-resolve pass mask, scaled so distinct passes show as distinct greys.</summary>
    PassId,
}
