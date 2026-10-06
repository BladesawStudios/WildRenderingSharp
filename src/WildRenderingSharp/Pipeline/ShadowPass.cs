using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Renders the shadow map by feeding the REAL G-buffer vertex shader a light-space
/// <c>Context</c> - no separate depth shader is needed, since the vertex stage transforms by
/// <c>cViewProj</c> regardless of which pass is running and this FBO has no colour attachment.
/// Blended shapes are included (they still cast a shadow even though they're excluded from the
/// deferred G-buffer). Mirrors the shadow-map half of <c>render_scene</c>/<c>build_light_matrices</c>.
/// </summary>
public sealed class ShadowPass
{
    readonly GL _gl;

    public ShadowPass(GL gl) => _gl = gl;

    public readonly record struct LightMatrices(Vector4[] View3Rows, Vector4[] Proj, Vector4[] ViewProj);

    /// <summary>Orthographic light view/proj tightly fitted to the model's (rotated) bounding sphere.</summary>
    public static LightMatrices BuildLightMatrices(Vector3 lo, Vector3 hi, Vector3 sunWorld)
    {
        var center = (lo + hi) * 0.5f;
        float radius = (hi - lo).Length() * 0.5f + 1e-4f;
        var eye = center + sunWorld * (radius * 2.5f);
        var f = Vector3.Normalize(center - eye);
        var up = MathF.Abs(f.Z) < 0.95f ? new Vector3(0, 0, 1) : new Vector3(0, 1, 0);
        var r = Vector3.Normalize(Vector3.Cross(f, up));
        var u = Vector3.Cross(r, f);

        Vector4[] view =
        [
            new(r.X, r.Y, r.Z, -Vector3.Dot(r, eye)),
            new(u.X, u.Y, u.Z, -Vector3.Dot(u, eye)),
            new(-f.X, -f.Y, -f.Z, Vector3.Dot(f, eye)),
            new(0, 0, 0, 1),
        ];
        const float near = 0.01f;
        float far = radius * 5.0f;
        float scale = 1f / radius;
        Vector4[] proj =
        [
            new(scale, 0, 0, 0),
            new(0, scale, 0, 0),
            new(0, 0, -2f / (far - near), -(far + near) / (far - near)),
            new(0, 0, 0, 1),
        ];
        var viewProj = Mat4Math.Multiply(proj, view);
        return new LightMatrices(view[..3], proj, viewProj);
    }

    /// <summary>Draws every placed actor's shapes into the SAME shadow target (every actor casts/receives shadows together, correctly), rebinding each actor's own skinning UBOs (<see cref="ActorDrawGroup"/>) before its own shapes.</summary>
    public void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups, ShaderProgramCache programs)
    {
        targets.BindShadowTarget();
        _gl.ClearDepth(1.0);
        _gl.Clear(ClearBufferMask.DepthBufferBit);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.DepthMask(true);

        // Depth-only target, no colour attachment - the real G-buffer fragment shader's actual
        // output is thrown away here regardless, but it still SAMPLES every one of the material's
        // textures and runs its full lighting/normal-map math to produce that discarded output,
        // for every shape, every frame. GBufferPass's own Z-prepass already solved exactly this
        // problem for its Z-only-then-EQUAL-test trick: a shape with a resolved Z-only program is
        // the real game's own cheap depth-only variant (still correctly alpha-testing a cutout
        // material, since it's the same shader the game uses for its own depth prepass) - reusing
        // it here writes the identical depth value for a fraction of the fragment cost. Only a
        // shape with no resolved Z-only variant falls back to the full G-buffer program, same as
        // GBufferPass's own fallback.
        foreach (var group in groups)
        {
            group.BindUbos(resources);
            foreach (var sh in group.Shapes)
            {
                group.Draw(_gl, programs, sh, sh.HasZOnly ? ShapeProgram.ZOnly : ShapeProgram.GBuffer);
            }
        }
    }
}
