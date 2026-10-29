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
    // With snapTexels, the centre is moved to a whole texel of a map that size along the light's own axes, so a map that follows the camera shifts by whole texels and a
    // shadow's edge does not crawl as it moves.
    public static LightMatrices BuildLightMatrices(Vector3 lo, Vector3 hi, Vector3 sunWorld, int snapTexels = 0)
    {
        var center = (lo + hi) * 0.5f;
        float radius = (hi - lo).Length() * 0.5f + 1e-4f;
        if (snapTexels > 0)
            center = SnapToTexels(center, 2f * radius / snapTexels, sunWorld);
        var eye = center + sunWorld * (radius * 2.5f);
        // Z is up, except when the sun is near-vertical and would look straight down it.
        var up = MathF.Abs(Vector3.Normalize(sunWorld).Y) < 0.95f ? Vector3.UnitY : -Vector3.UnitZ;

        var view = Matrix4x4.CreateLookAt(eye, center, up);
        var proj = Matrix4x4.CreateOrthographic(radius * 2f, radius * 2f, 0.01f, radius * 5f) * ClipSpace.ZeroToOneDepthToGl;
        return new LightMatrices(view, proj, view * proj);
    }

    static Vector3 SnapToTexels(Vector3 center, float texel, Vector3 sunWorld)
    {
        // The light's right and up, as CreateLookAt builds them below.
        var toEye = Vector3.Normalize(sunWorld);
        var up = MathF.Abs(toEye.Y) < 0.95f ? Vector3.UnitY : -Vector3.UnitZ;
        var right = Vector3.Normalize(Vector3.Cross(up, toEye));
        var above = Vector3.Cross(toEye, right);

        float x = Vector3.Dot(center, right), y = Vector3.Dot(center, above);
        return center + right * (MathF.Round(x / texel) * texel - x) + above * (MathF.Round(y / texel) * texel - y);
    }

    // statics draws into the cascade's array of unmoving casters; clear false draws over what the target holds.
    public void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups, ShapeDrawer drawer, int cascade = -1,
        bool statics = false, bool clear = true)
    {
        if (cascade >= 0)
            targets.BindShadowCascadeTarget(cascade, statics);
        else
            targets.BindShadowTarget();
        if (clear)
        {
            _gl.ClearDepth(1.0);
            _gl.Clear(ClearBufferMask.DepthBufferBit);
        }
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.DepthMask(true);

        // Depth only: a shape with a Z-only program uses it (the game's own cheap variant, still alpha-testing cutouts), the rest fall back to the G-buffer program.
        drawer.Merged.BeginCollecting();
        foreach (var group in groups)
        {
            group.BindUbos(resources);
            foreach (var sh in group.Shapes)
            {
                group.Draw(drawer, sh, sh.HasZOnly ? ShapeProgram.ZOnly : ShapeProgram.GBuffer);
            }
        }
        drawer.Merged.Flush();
    }
}
