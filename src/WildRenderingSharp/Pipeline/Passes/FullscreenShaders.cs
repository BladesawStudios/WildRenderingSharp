using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Pipeline.Passes;

/// <summary>
/// Vertex stages shared by the fullscreen effects: one oversized triangle generated from <c>gl_VertexID</c>, so no vertex buffer is
/// needed.
/// </summary>
public static class FullscreenShaders
{
    public static readonly string Vertex450 = GlslFiles.Load("Pipeline/FullscreenShaders/Vertex450.vert");

    public static readonly string Vertex330 = GlslFiles.Load("Pipeline/FullscreenShaders/Vertex330.vert");
}
