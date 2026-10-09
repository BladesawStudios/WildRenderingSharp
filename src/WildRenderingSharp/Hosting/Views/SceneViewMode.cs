
namespace WildRenderingSharp.Hosting.Views;

public enum SceneViewMode
{
    Final,
    HdrPreview,
    Albedo,
    Normal,
    Shadow,
    AmbientOcclusion,
    PassId,
    Emission,
    GBuffer0,
    GBuffer2,
    GBuffer4,
    LinearDepth,
    FieldLightLayer0,
    FieldLightLayer1,
    CharaLightLayer0,

    /// <summary>What the last deferred resolve pass wrote, before it was composited (scaled down).</summary>
    LastResolve,
    AfterResolve,
    AfterForward,
    AfterFlare,

    /// <summary>What one deferred resolve pass wrote on its own (<see cref="SceneView.ResolvePassIndex"/>), shown in HDR.</summary>
    ResolvePass,
}
