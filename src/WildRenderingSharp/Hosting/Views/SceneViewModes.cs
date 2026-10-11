namespace WildRenderingSharp.Hosting.Views;

/// <summary>What each <see cref="SceneViewMode"/> shows and how it is drawn.</summary>
static class SceneViewModes
{
    // The views that go through grading and anti-aliasing; the raw diagnostic views skip both.
    public static bool IsGraded(this SceneViewMode mode) => mode is SceneViewMode.Final or SceneViewMode.HdrPreview or SceneViewMode.AfterResolve
        or SceneViewMode.AfterForward or SceneViewMode.AfterFlare or SceneViewMode.ResolvePass;

    // Whether the output's first row is the top of the image, which the raw views draw and the graded ones do not.
    public static bool IsTopDown(this SceneViewMode mode) => mode is SceneViewMode.Albedo or SceneViewMode.Normal or SceneViewMode.Shadow
        or SceneViewMode.AmbientOcclusion or SceneViewMode.Emission or SceneViewMode.GBuffer0 or SceneViewMode.GBuffer2 or SceneViewMode.GBuffer4
        or SceneViewMode.LinearDepth or SceneViewMode.FieldLightLayer0 or SceneViewMode.FieldLightLayer1 or SceneViewMode.CharaLightLayer0;

    // The modes that capture the frame between passes.
    public static bool NeedsStageSnapshots(this SceneViewMode mode) =>
        mode is SceneViewMode.AfterResolve or SceneViewMode.AfterForward or SceneViewMode.AfterFlare or SceneViewMode.ResolvePass;
}
