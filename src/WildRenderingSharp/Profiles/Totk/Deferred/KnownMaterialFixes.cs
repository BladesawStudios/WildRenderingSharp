using WildRenderingSharp.Graphics.Data;
using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Gpu;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>
/// Manual, individually verified corrections for game rendering behaviour the shader-driven pipeline cannot derive; see <see
/// cref="Rendering.LightingContext.EnableKnownMaterialFixes"/>.
/// </summary>
public sealed class KnownMaterialFixes : IDisposable
{
    readonly GL _gl;
    readonly uint _program;

    public const string EyeVisibilityMaskTextureName = "Cmn_Enemy_DungeonBoss_Eye_Alb";

    public static bool NeedsEyeVisibilityMaskFix(LoadedShape shape)
    {
        foreach (var sampler in shape.GBufferSamplers)
            if (sampler.Key == "_a0" && string.Equals(sampler.Texture.Name, EyeVisibilityMaskTextureName, StringComparison.Ordinal))
                return true;
        return false;
    }

    static readonly string VertexSource = GlslFiles.Load("Totk/Deferred/KnownMaterialFixes/Main.vert");

    static readonly string FragmentSource = GlslFiles.Load("Totk/Deferred/KnownMaterialFixes/Main.frag");

    public KnownMaterialFixes(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, VertexSource, FragmentSource, "known_material_fix_eye_visibility_mask");
    }

    public unsafe void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ActorDrawGroup> groups,
        Matrix4x4 viewProjFlipped, float emissionScale, float exposure)
    {
        var flaggedGroups = groups
            .Select(g => (Group: g, Flagged: g.Shapes.Where(NeedsEyeVisibilityMaskFix).ToList()))
            .Where(x => x.Flagged.Count > 0)
            .ToList();
        if (flaggedGroups.Count == 0)
            return;

        targets.BindColorAndDepthTarget(targets.Scene, targets.GBufferDepth);
        // Depth-tested with a small negative polygon offset, the standard decal technique. This redraws geometry the G-buffer already rasterised through a hand-written vertex shader
        // whose clip position can land a few ULPs off the stored depth (see ForwardPass). A bare LEQUAL test flickered (z-fighting); no test at all drew over genuine occluders
        // (eyelid and head geometry). The offset nudges depth toward the camera by a couple of the buffer's smallest steps: enough to beat the self-mismatch, far short of a real occluder.
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(false);
        _gl.Enable(EnableCap.PolygonOffsetFill);
        _gl.PolygonOffset(-1f, -4f);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.Blend);

        _gl.UseProgram(_program);
        _gl.BindTextureUniform(_program, "tex_alb", 0, targets.GBuffer[1].Handle);
        _gl.BindTextureUniform(_program, "tex_emis", 1, targets.GBuffer[5].Handle);
        _gl.SetVec2(_program, "uViewportSize", new Vector2(targets.Width, targets.Height));
        _gl.SetFloat(_program, "uEmission", emissionScale);
        _gl.SetFloat(_program, "uEmissionExposureRcp", exposure > 1e-4f ? 1f / exposure : 1f);
        _gl.SetMat4(_program, "uViewProj", viewProjFlipped);

        foreach (var (group, flagged) in flaggedGroups)
        {
            // A character's eye fix: batches of placed world objects have none to fix and no single model matrix to draw it with.
            if (group.Batch is not null)
                continue;
            group.BindUbos(resources);
            var mvp = GpuMatrix.FromRows(group.ModelMatrixRows) * viewProjFlipped;

            foreach (var shape in flagged)
            {
                _gl.SetMat4(_program, "uMVP", mvp);
                _gl.Uniform1(_gl.GetUniformLocation(_program, "uSkinCount"), shape.VertexSkinCount);
                _gl.BindVertexArray(shape.PassIdVao);
                _gl.DrawElements(PrimitiveType.Triangles, (uint)shape.IndexCount, DrawElementsType.UnsignedInt, null);
            }
        }

        _gl.Disable(EnableCap.PolygonOffsetFill);
        _gl.DepthMask(true);
        _gl.DepthFunc(DepthFunction.Less);
    }

    public void Dispose() => _gl.DeleteProgram(_program);
}
