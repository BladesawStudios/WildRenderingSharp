using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Rendering.Cameras;

namespace WildRenderingSharp.Pipeline.Shadows;

/// <summary>
/// Renders the shadow map by running the G-buffer vertex shader with a light-space context. It needs no separate depth shader because the
/// framebuffer has no colour attachment.
/// </summary>
internal sealed class ShadowPass
{
    readonly GL _gl;

    public ShadowPass(GL gl) => _gl = gl;

    public readonly record struct LightMatrices(Matrix4x4 View, Matrix4x4 Proj, Matrix4x4 ViewProj);

    // An orthographic sun camera framing the bounding sphere of the box lo..hi: the eye sits 2.5 radii out along the sun direction looking at the
    // centre, and the depth range covers 5 radii.
    public static LightMatrices BuildLightMatrices(Vector3 lo, Vector3 hi, Vector3 sunWorld)
    {
        var center = (lo + hi) * 0.5f;
        float radius = (hi - lo).Length() * 0.5f + 1e-4f;
        var eye = center + sunWorld * (radius * 2.5f);
        // Z is up, except when the sun is near-vertical and would look straight down it.
        var up = MathF.Abs(Vector3.Normalize(sunWorld).Y) < 0.95f ? Vector3.UnitY : -Vector3.UnitZ;

        var view = Matrix4x4.CreateLookAt(eye, center, up);
        var proj = Matrix4x4.CreateOrthographic(radius * 2f, radius * 2f, 0.01f, radius * 5f) * ClipSpace.ZeroToOneDepthToGl;
        return new LightMatrices(view, proj, view * proj);
    }

    public void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups, ShapeDrawer drawer, int cascade = -1)
    {
        if (cascade >= 0)
            targets.BindShadowCascadeTarget(cascade);
        else
            targets.BindShadowTarget();
        _gl.ClearDepth(1.0);
        _gl.Clear(ClearBufferMask.DepthBufferBit);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.DepthMask(true);

        // Depth only: a shape with a Z-only program uses it (the game's own cheap variant, still alpha-testing cutouts), the rest fall back to the G-buffer program.
        foreach (var group in groups)
        {
            group.BindUbos(resources);
            foreach (var sh in group.Shapes)
            {
                group.Draw(drawer, sh, sh.HasZOnly ? ShapeProgram.ZOnly : ShapeProgram.GBuffer);
            }
        }
    }
}
