namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Vertex stages shared by the fullscreen effects: one oversized triangle generated from <c>gl_VertexID</c>, so no vertex buffer is
/// needed. Draw three vertices.
/// </summary>
public static class FullscreenShaders
{
    /// <summary>Writes <c>vUV</c> over [0, 1] across the screen.</summary>
    public const string Vertex450 = """
        #version 450 core
        out vec2 vUV;
        void main() {
            float x = -1.0 + float((gl_VertexID & 1) * 4);
            float y = -1.0 + float((gl_VertexID & 2) * 2);
            vUV = vec2(x, y) * 0.5 + 0.5;
            gl_Position = vec4(x, y, 0.0, 1.0);
        }
        """;

    /// <summary>The same triangle for the passes whose fragment stage is GLSL 330.</summary>
    public const string Vertex330 = """
        #version 330 core
        out vec2 vUV;
        void main()
        {
            vUV = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
            gl_Position = vec4(vUV * 2.0 - 1.0, 0.0, 1.0);
        }
        """;
}
