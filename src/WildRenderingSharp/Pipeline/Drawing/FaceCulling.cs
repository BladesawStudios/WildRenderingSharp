using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Manifests;

namespace WildRenderingSharp.Pipeline.Drawing;

/// <summary>Applies the face a material authors it shows, for passes that draw one shape at a time.</summary>
internal static class FaceCulling
{
    // A double-sided lens shaded on both faces became a solid disc, its far side being a fresnel-opaque back face.
    public static void Apply(GL gl, RenderState state)
    {
        if (state.CulledFace is not { } face)
        {
            gl.Disable(EnableCap.CullFace);
            return;
        }
        gl.Enable(EnableCap.CullFace);
        gl.FrontFace(FrontFaceDirection.Ccw);
        gl.CullFace(face);
    }

    public static void Reset(GL gl)
    {
        gl.Disable(EnableCap.CullFace);
        gl.FrontFace(FrontFaceDirection.Ccw);
        gl.CullFace(TriangleFace.Back);
    }
}
