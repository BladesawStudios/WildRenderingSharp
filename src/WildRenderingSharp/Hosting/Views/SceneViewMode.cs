
namespace WildRenderingSharp.Hosting.Views;

/// <summary>What a scene view shows: the finished frame, an HDR view, or one of the buffers a frame is made of.</summary>
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

    // What the last deferred resolve pass wrote, before it was composited (scaled down).
    LastResolve,
    AfterResolve,
    AfterForward,
    AfterFlare,

    // What one deferred resolve pass wrote on its own (ResolvePassIndex), shown in HDR.
    ResolvePass,
}
